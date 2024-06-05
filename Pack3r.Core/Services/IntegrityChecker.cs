using System.Collections.Concurrent;
using NAudio.Wave;
using Pack3r.Extensions;
using Pack3r.Logging;
using Pack3r.Models;

namespace Pack3r.Services;

public interface IIntegrityChecker
{
    void Log();
    void CheckIntegrity(IAsset asset);
}

public sealed class IntegrityChecker(ILogger<IntegrityChecker> logger) : IIntegrityChecker
{
    public void Log()
    {
        if (!_jpgs.IsEmpty)
        {
            string paths = string.Join(Environment.NewLine, _jpgs.Select(p => $"\t{p}"));
            logger.Warn($"Found potentially progressive JPGs which are unsupported on 2.60b: {Environment.NewLine}{paths}");
        }

        if (!_tgas.IsEmpty)
        {
            string paths = string.Join(Environment.NewLine, _tgas.Select(p => $"\t{p}"));
            logger.Warn($"Found top-left pixel ordered TGAs which are drawn upside down on 2.60b clients: {Environment.NewLine}{paths}");
        }

        foreach (var (path, warning) in _wavs)
        {
            logger.Warn($"File '{path}' {warning}");
        }
    }

    private readonly ConcurrentQueue<(string path, string warning)> _wavs = [];
    private readonly ConcurrentBag<string> _jpgs = [];
    private readonly ConcurrentBag<string> _tgas = [];

    private readonly ConcurrentDictionary<(string, string), object?> _handled = [];

    public void CheckIntegrity(IAsset asset)
    {
        if (!CanCheckIntegrity(asset.FullPath))
            return;

        if (!_handled.TryAdd((asset.Name, ""), null))
            return;

        CheckIntegrityCore(asset);
    }

    private static bool CanCheckIntegrity(string path)
    {
        ReadOnlySpan<char> extension = path.GetExtension();

        return extension.EqualsF(".tga")
            || extension.EqualsF(".jpg")
            || extension.EqualsF(".wav");
    }

    private void CheckIntegrityCore(IAsset asset)
    {
        using Stream stream = asset.OpenRead();
        string fullPath = asset.FullPath;
        ReadOnlySpan<char> extension = fullPath.GetExtension();

        if (extension.EqualsF(".tga"))
        {
            VerifyTga(fullPath, stream);
            return;
        }

        if (extension.EqualsF(".jpg"))
        {
            VerifyJpg(fullPath, stream);
            return;
        }

        if (extension.EqualsF(".wav"))
        {
            if (VerifyWav(fullPath, stream) is string error)
                _wavs.Enqueue((fullPath.NormalizePath(), error));
        }
    }

    private void VerifyTga(string path, Stream stream)
    {
        const int index = 17;
        int value = -1;

        if (stream.CanSeek)
        {
            stream.Position = index;
            value = stream.ReadByte();
        }
        else if (stream.CanRead)
        {
            scoped Span<byte> buffer = stackalloc byte[256];

            int read = stream.ReadAtLeast(buffer, minimumBytes: index, throwOnEndOfStream: false);

            if (read >= index)
            {
                value = buffer[index];
            }
        }

        if (value == 0x20)
        {
            _tgas.Add(path.NormalizePath());
        }
    }

    private void VerifyJpg(string fullPath, Stream stream)
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

    private string? VerifyWav(string path, Stream stream)
    {
        try
        {
            using var reader = new WaveFileReader(stream);
            WaveFormat fmt = reader.WaveFormat;

            if (fmt.Encoding != WaveFormatEncoding.Pcm)
                return $"has invalid encoding {fmt.Encoding} instead of PCM";

            List<string> errors = [];

            if (fmt.Channels != 1)
                errors.Add($"expected mono instead of {fmt.Channels} channels");

            if (fmt.BitsPerSample != 16)
                errors.Add($"expected 16bit instead of {fmt.BitsPerSample}bit");

            if (fmt.SampleRate is not 44100 and not 44100 / 2 and not 44100 / 4)
                errors.Add($"expected multiple of 44.1 kHz instead of {fmt.SampleRate}");

            if (errors.Count > 0)
                return $"has invalid audio format: {string.Join(" | ", errors)}";
        }
        catch (Exception e)
        {
            logger.Exception(e, $"Could not verify WAV file integrity: {path}");
        }

        return null;
    }

}
