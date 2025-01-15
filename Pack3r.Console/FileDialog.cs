using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Pack3r.Console;

#nullable disable

// mostly from: https://gist.github.com/gotmachine/4ffaf7837f9fbb0ab4a648979ee40609
[SupportedOSPlatform("windows")]
internal partial class OpenFileDialog
{
    private const int MAX_FILE_LENGTH = 2048;

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileName(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct OpenFileName()
    {
        public int structSize = 0;
        public nint dlgOwner = nint.Zero;
        public nint instance = nint.Zero;
        public char* filter;
        public char* customFilter;
        public int maxCustFilter = 0;
        public int filterIndex = 0;
        public nint file;
        public int maxFile = 0;
        public char* fileTitle;
        public int maxFileTitle = MAX_FILE_LENGTH;
        public char* initialDir;
        public char* title;
        public int flags = 0;
        public short fileOffset = 0;
        public short fileExtension = 0;
        public char* defExt;
        public nint custData = nint.Zero;
        public nint hook = nint.Zero;
        public char* templateName;
        public nint reservedPtr = nint.Zero;
        public int reservedInt = 0;
        public int flagsEx = 0;
    }

    private enum OpenFileNameFlags
    {
        OFN_DONTADDTORECENT = 0x02000000,
        OFN_HIDEREADONLY = 0x4,
        OFN_FORCESHOWHIDDEN = 0x10000000,
        OFN_ALLOWMULTISELECT = 0x200,
        OFN_EXPLORER = 0x80000,
        OFN_FILEMUSTEXIST = 0x1000,
        OFN_PATHMUSTEXIST = 0x800
    }

    private bool Multiselect { get; set; } = false;
    private string InitialDirectory { get; set; } = null;
    private bool Success { get; set; }
    private string[] Files { get; set; }

    public static bool OpenFile(out string file)
    {
        OpenFileDialog dialog = new();

        dialog.OpenDialog();
        if (dialog.Success && dialog.Files.Length > 0)
        {
            file = dialog.Files[0];
            return true;
        }

        file = null;
        return false;
    }

    private void OpenDialog()
    {
        Thread thread = new(ShowOpenFileDialog);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private unsafe void ShowOpenFileDialog()
    {
        Success = false;
        Files = null;

        fixed (char* filter = "Radiant map files (*.map, *.reg)\0*.map;*.reg\0")
        fixed (char* fileTitle = new char[MAX_FILE_LENGTH])
        fixed (char* initialDir = InitialDirectory)
        fixed (char* windowTitle = "Open map file (run as CLI for more options)")
        {
            OpenFileName ofn = new();

            try
            {
                ofn.structSize = Marshal.SizeOf(ofn);
                ofn.filter = filter;
                ofn.fileTitle = fileTitle;
                ofn.initialDir = initialDir;
                ofn.title = windowTitle;
                ofn.flags = (int)(
                    OpenFileNameFlags.OFN_DONTADDTORECENT |
                    OpenFileNameFlags.OFN_HIDEREADONLY |
                    OpenFileNameFlags.OFN_EXPLORER |
                    OpenFileNameFlags.OFN_FILEMUSTEXIST |
                    OpenFileNameFlags.OFN_PATHMUSTEXIST
                    );

                // Create buffer for file names
                ofn.file = Marshal.AllocHGlobal(MAX_FILE_LENGTH * Marshal.SystemDefaultCharSize);
                ofn.maxFile = MAX_FILE_LENGTH;

                // Initialize buffer with NULL bytes
                new Span<byte>((void*)ofn.file, Marshal.SystemDefaultCharSize).Clear();

                if (Multiselect)
                {
                    ofn.flags |= (int)OpenFileNameFlags.OFN_ALLOWMULTISELECT;
                }

                Success = GetOpenFileName(ref ofn);

                if (Success)
                {
                    nint filePointer = ofn.file;
                    long pointer = filePointer;
                    string file = Marshal.PtrToStringAuto(filePointer);
                    List<string> strList = [];

                    // Retrieve file names
                    while (file.Length > 0)
                    {
                        strList.Add(file);

                        pointer += file.Length * Marshal.SystemDefaultCharSize + Marshal.SystemDefaultCharSize;
                        filePointer = (nint)pointer;
                        file = Marshal.PtrToStringAuto(filePointer);
                    }

                    if (strList.Count > 1)
                    {
                        Files = new string[strList.Count - 1];
                        for (int i = 1; i < strList.Count; i++)
                        {
                            Files[i - 1] = Path.Combine(strList[0], strList[i]);
                        }
                    }
                    else
                    {
                        Files = [.. strList];
                    }
                }
            }
            finally
            {
                if (ofn.file != IntPtr.Zero)
                    Marshal.FreeHGlobal(ofn.file);
            }
        }
    }
}
