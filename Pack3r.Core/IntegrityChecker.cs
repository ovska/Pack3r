using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;
using NAudio.Wave;
using Pack3r.Extensions;
using Pack3r.Logging;

namespace Pack3r;

internal static class IntegrityChecker
{
    public static void Log(ILogger logger)
    {
        if (!_jpgs.IsEmpty)
        {
            string paths = string.Join('\n', _jpgs.Select(p => $"\t{p}"));
            logger.Warn($"Found potentially progressive JPGs which are unsupported on 2.60b: \n{paths}");
        }

        if (!_tgas.IsEmpty)
        {
            string paths = string.Join('\n', _tgas.Select(p => $"\t{p}"));
            logger.Warn($"Found top-left pixel ordered TGAs which are drawn upside down on 2.60b clients: \n{paths}");
        }

        foreach (var (path, warning) in _wavs)
        {
            logger.Warn($"File '{path}' {warning}");
        }
    }

    private static readonly ConcurrentQueue<(string path, string warning)> _wavs = [];

    private static readonly ConcurrentBag<string> _jpgs = [];
    private static readonly ConcurrentBag<string> _tgas = [];

    private static ConcurrentDictionary<(string, string), object?> _handled = [];

    public static void CheckIntegrity(string fullPath)
    {
        if (!CanCheckIntegrity(fullPath))
            return;

        if (!_handled.TryAdd((fullPath, ""), null))
            return;

        using FileStream stream = File.OpenRead(fullPath);
        CheckIntegrityCore(fullPath, stream);
    }

    public static void CheckIntegrity(string archivePath, ZipArchiveEntry entry)
    {
        if (!CanCheckIntegrity(entry.FullName))
            return;

        if (!_handled.TryAdd((archivePath, entry.FullName), null))
            return;

        using Stream stream = entry.Open();
        CheckIntegrityCore(Path.Combine(archivePath, entry.FullName).NormalizePath(), stream);
    }

    private static bool CanCheckIntegrity(string path)
    {
        ReadOnlySpan<char> extension = path.GetExtension();

        return extension.EqualsF(".tga")
            || extension.EqualsF(".jpg")
            || extension.EqualsF(".wav");
    }

    private static void CheckIntegrityCore(
        string fullPath,
        Stream stream)
    {
        ReadOnlySpan<char> extension = fullPath.GetExtension();

        if (extension.EqualsF(".tga"))
        {
            if (ReadAt(stream, 17) == 0x20)
            {
                _tgas.Add(fullPath.NormalizePath());
                //return "has top-left ordered TGA pixel format, which will cause the texture to be drawn upside down on 2.60b clients";
            }

            return;
        }

        if (extension.EqualsF(".jpg"))
        {
            VerifyJpg(fullPath, stream);
            return;
        }

        if (extension.EqualsF(".wav"))
        {
            if (VerifyWav(stream) is string error)
                _wavs.Enqueue((fullPath.NormalizePath(), error));
        }
    }

    private static int ReadAt(Stream stream, int index)
    {
        byte[]? toReturn = null;

        try
        {
            if (stream.CanSeek)
            {
                stream.Position = index;
                return stream.ReadByte();
            }
            else if (stream.CanRead)
            {
                scoped Span<byte> buffer;

                if (index <= 256)
                {
                    buffer = stackalloc byte[256];
                }
                else
                {
                    toReturn = ArrayPool<byte>.Shared.Rent(index);
                    buffer = toReturn;
                }

                int read = stream.ReadAtLeast(buffer, minimumBytes: index, throwOnEndOfStream: false);

                if (read >= index)
                {
                    return buffer[index];
                }
            }
        }
        finally
        {
            if (toReturn != null)
                ArrayPool<byte>.Shared.Return(toReturn);
        }

        return -1;
    }

    private static void VerifyJpg(string fullPath, Stream stream)
    {
        using var ms = Global.StreamManager.GetStream(nameof(VerifyJpg), requiredSize: 1024 * 1024);

        stream.CopyTo(ms);

        if (!ms.TryGetBuffer(out ArraySegment<byte> buffer))
            buffer = ms.ToArray();

        ReadOnlySpan<byte> bytes = buffer;

        // A progressive DCT-based JPEG can be identified by bytes “0xFF, 0xC2″
        // Also, progressive JPEG images usually contain .. a couple of “Start of Scan” matches (bytes: “0xFF, 0xDA”)
        // source: https://superuser.com/a/1010777
        ReadOnlySpan<byte> DCTbytes = [0xFF, 0xC2];
        ReadOnlySpan<byte> SOSbytes = [0xFF, 0xDA];

        if (bytes.IndexOf(DCTbytes) >= 0 && bytes.Count(SOSbytes) >= 6)
        {
            _jpgs.Add(fullPath.NormalizePath());
            //return "is potentially a progressive JPG, which is not supported on 2.60b clients";
        }
    }

    private static string? VerifyWav(Stream stream)
    {
        using var reader = new WaveFileReader(stream);
        WaveFormat fmt = reader.WaveFormat;

        if (fmt.Encoding != WaveFormatEncoding.Pcm)
            return $"has invalid encoding {fmt.Encoding} instead of PCM";

        List<string>? errors = null;

        if (fmt.Channels != 1)
            (errors ??= []).Add($"expected mono instead of {fmt.Channels} channels");

        if (fmt.BitsPerSample != 16)
            (errors ??= []).Add($"expected 16bit instead of {fmt.BitsPerSample}bit");

        if (fmt.SampleRate is not 44100 and not 44100 / 2 and not 44100 / 4)
            (errors ??= []).Add($"expected multiple of 44.1 kHz instead of {fmt.SampleRate}");

        if (errors is { Count: > 0 })
            return $"has invalid audio format: {string.Join(" | ", errors)}";

        return null;
    }

}
