using Pack3r.Extensions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Pack3r;

public static class Global
{
    public const int MAX_QPATH = 64;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureQPathLength(string path)
    {
        if (path.Length > MAX_QPATH)
            ThrowQPathTooLong(path.AsMemory());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureQPathLength(ReadOnlyMemory<char> path)
    {
        if (path.Length > MAX_QPATH)
            ThrowQPathTooLong(path);
    }

    [DoesNotReturn]
    public static void ThrowQPathTooLong(ReadOnlyMemory<char> path)
    {
        throw new InvalidDataException($"QPath is too long ({path.Length}, max 64): '{path.ToString().NormalizePath()}'");
    }

    public static readonly Lock ConsoleLock = new();

    public static string Version { get; } = typeof(Global).Assembly.GetName().Version?.ToString(3) ?? "?.?.?";

    public static string Disclaimer { get; } = $"// Modified by Pack3r {Version}";

    public static ParallelOptions ParallelOptions(CancellationToken cancellationToken)
    {
        return new ParallelOptions
        {
            MaxDegreeOfParallelism = Debugger.IsAttached ? 1 : -1,
            CancellationToken = cancellationToken,
        };
    }

    /// <summary>
    /// <see cref="Parallel.For(int, int, System.Threading.Tasks.ParallelOptions, Action{int})"/> over
    /// <c>0..count</c>, that throws the first exception thrown by the body as is, instead of wrapped in an
    /// <see cref="AggregateException"/>.
    /// </summary>
    public static void ParallelFor(int count, CancellationToken cancellationToken, Action<int> body)
    {
        try
        {
            Parallel.For(0, count, ParallelOptions(cancellationToken), body);
        }
        catch (AggregateException e) when (e.InnerExceptions.Count > 0)
        {
            ExceptionDispatchInfo.Throw(e.InnerExceptions[0]);
        }
    }
}
