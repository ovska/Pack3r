using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.IO;
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

    public static void CheckIntegrity(string archivePath, ZipArchiveEntry entry)
    {
        if (!CanCheckIntegrity(entry.FullName))
            return;

        using Stream stream = entry.Open();

        if (CheckIntegrityCore(entry.FullName.GetExtension(), stream) is string warning)
        {
            _values.Enqueue((Path.Join(archivePath, entry.FullName).NormalizePath(), warning));
        }
    }

    private static bool CanCheckIntegrity(string path)
    {
        ReadOnlySpan<char> extension = path.GetExtension();

        return extension.EqualsF(".tga")
            || extension.EqualsF(".jpg")
            || extension.EqualsF(".wav");
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

        if (extension.EqualsF(".jpg"))
        {
            return VerifyJpg();
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

        string? VerifyJpg()
        {
            using var ms = _manager.GetStream(null, requiredSize: 1024 * 1024);

            stream.CopyTo(ms);

            if (!ms.TryGetBuffer(out ArraySegment<byte> buffer))
                return null;

            ReadOnlySpan<byte> bytes = buffer;

            // A progressive DCT-based JPEG can be identified by bytes “0xFF, 0xC2″
            // Also, progressive JPEG images usually contain .. a couple of “Start of Scan” matches (bytes: “0xFF, 0xDA”)
            // source: https://superuser.com/a/1010777
            ReadOnlySpan<byte> DCTbytes = [0xFF, 0xC2];
            ReadOnlySpan<byte> SOSbytes = [0xFF, 0xDA];

            if (bytes.IndexOf(DCTbytes) >= 0 && bytes.Count(SOSbytes) >= 6)
            {
                return "is potentially a progressive JPG, which is not supported on 2.60b clients";
            }

            return null;
        }
    }

    private static readonly RecyclableMemoryStreamManager _manager = new(new RecyclableMemoryStreamManager.Options
    {
        AggressiveBufferReturn = true,
        GenerateCallStacks = Debugger.IsAttached,
    });
}
