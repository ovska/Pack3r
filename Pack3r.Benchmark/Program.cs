using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.IO;
using SharpCompress.Archives;

BenchmarkRunner.Run<CompressBench>();

[DryJob]
[MemoryDiagnoser]
public class CompressBench
{
    private static readonly (string path, string name)[] _files =
        Directory
            .GetFiles("C:\\Temp\\ET\\map\\ET\\etmain\\void.pk3dir\\maps\\void_b2")
            .Select(p => (p, Path.GetFileName(p)))
            .ToArray();

    private readonly byte[] _buffer = new byte[4096];

    [Benchmark(Baseline = true)]
    public void IOCompression()
    {
        using var archive = new ZipArchive(Stream.Null, ZipArchiveMode.Create);

        foreach (var (file, name) in _files)
        {
            archive.CreateEntryFromFile(file, name);
        }
    }

    [Benchmark(Baseline = false)]
    public async Task IOCompression_P()
    {
        using var archive = new ZipArchive(Stream.Null, ZipArchiveMode.Create);

        using var sp = new SemaphoreSlim(1, 1);

        await Parallel.ForEachAsync(_files, async (f, ct) =>
        {
            var (file, name) = f;

            using var ms = _rms.GetStream();

            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            {
                await fs.CopyToAsync(ms, ct);
            }

            await sp.WaitAsync(ct);
            var e = archive.CreateEntry(name);
            using (var s = e.Open())
            {
                ms.Position = 0;
                ms.CopyTo(s);
            }
            sp.Release();
        });
    }

    private static readonly RecyclableMemoryStreamManager _rms = new();

    [Benchmark(Baseline = false)]
    public void SharpComp()
    {
        using var archive = SharpCompress.Archives.Zip.ZipArchive.Create();

        foreach (var (file, name) in _files)
        {
            archive.AddEntry(name, new FileInfo(file));
        }

        archive.SaveTo(Stream.Null);
    }

    [Benchmark(Baseline = false)]
    public void DotNetZip()
    {
        using var zip = new ZipOutputStream(Stream.Null);

        foreach (var (file, name) in _files)
        {
            ZipEntry entry = new(name);
            zip.PutNextEntry(entry);
            using var fs = File.OpenRead(file);
            StreamUtils.Copy(fs, zip, _buffer);
            zip.CloseEntry();
        }

        zip.Close();
    }
}