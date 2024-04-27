using System.Runtime.CompilerServices;

namespace Pack3r.Logging;

public interface ILogger<out T> : ILogger;

public interface ILogger
{
    void Log(LogLevel level, ref DefaultInterpolatedStringHandler handler);
    void Exception(Exception? e, string message);
    void Drain();

    public void Debug(ref DefaultInterpolatedStringHandler handler) => Log(LogLevel.Debug, ref handler);
    public void Info(ref DefaultInterpolatedStringHandler handler) => Log(LogLevel.Info, ref handler);
    public void Warn(ref DefaultInterpolatedStringHandler handler) => Log(LogLevel.Warn, ref handler);
    public void Error(ref DefaultInterpolatedStringHandler handler) => Log(LogLevel.Error, ref handler);
    public void Fatal(ref DefaultInterpolatedStringHandler handler) => Log(LogLevel.Fatal, ref handler);
    public void System(ref DefaultInterpolatedStringHandler handler) => Log((LogLevel)byte.MaxValue, ref handler);
}
