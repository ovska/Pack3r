namespace Pack3r.IO;

public interface ITempDirectoryProvider
{
    TempDirectory Create();
}

public readonly struct TempDirectory(DirectoryInfo value) : IDisposable
{
    public DirectoryInfo Value { get; } = value;
    public string Path => Value.FullName;

    public void Dispose()
    {
        Value.Delete(recursive: true);
    }
}
