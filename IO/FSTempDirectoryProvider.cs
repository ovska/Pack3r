namespace Pack3r.IO;

public class FSTempDirectoryProvider : ITempDirectoryProvider
{
    public TempDirectory Create()
    {
        var dir = Directory.CreateTempSubdirectory("pack3r");
        return new TempDirectory(dir);
    }
}
