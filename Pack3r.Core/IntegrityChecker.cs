using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using NAudio.Wave;
using Pack3r.Extensions;
using Pack3r.Logging;

namespace Pack3r;

internal static class IntegrityChecker
{
    public static void Log(ILogger logger)
    {
        while (_values.TryDequeue(out var value))
        {
            logger.Warn($"File '{value.path}' {value.warning}");
        }
    }

    private static readonly ConcurrentQueue<(string path, string warning)> _values = [];

    public static void CheckIntegrity(string fullPath)
    {
        if (!CanCheckIntegrity(fullPath))
            return;

        using FileStream stream = File.OpenRead(fullPath);

        if (CheckIntegrityCore(fullPath.GetExtension(), stream) is string warning)
        {
            _values.Enqueue((fullPath.NormalizePath(), warning));
        }
    }

    public static void CheckIntegrity(string archivePath, string entryPath, Stream stream)
    {
        if (!CanCheckIntegrity(entryPath))
            return;

        try
        {
            if (CheckIntegrityCore(entryPath.GetExtension(), stream) is string warning)
            {
                _values.Enqueue((Path.Join(archivePath, entryPath).NormalizePath(), warning));
            }
        }
        finally
        {
            stream.Position = 0;
        }
    }

    private static bool CanCheckIntegrity(string path)
    {
        ReadOnlySpan<char> extension = path.GetExtension();
        return extension.EqualsF(".tga") || extension.EqualsF(".wav");
    }

    private static string? CheckIntegrityCore(ReadOnlySpan<char> extension, Stream stream)
    {
        if (extension.EqualsF(".tga"))
        {
            if (ReadAt(17) == 0x20)
            {
                return "has top-left ordered TGA pixel format, which will cause the texture to be drawn upside down on 2.60b clients";
            }

            return null;
        }

        if (extension.EqualsF(".wav"))
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
        }

        return null;

        int ReadAt(int index)
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

                    int read = stream.ReadAtLeast(buffer, index, throwOnEndOfStream: false);

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
    }
}
