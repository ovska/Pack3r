using System.Runtime.CompilerServices;
using Pack3r.Extensions;

namespace Pack3r.Logging;

public sealed class NullLogger<T> : ILogger<T>
{
    public static readonly NullLogger<T> Instance = new();
    public void Log(LogLevel level, ref DefaultInterpolatedStringHandler handler) => handler.Clear();
    public void Exception(Exception? e, string message) { }
    public void Drain() { }
}
