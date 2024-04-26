using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Pack3r.Extensions;

namespace Pack3r;

public enum LogLevel { Debug, Info, Warn, Error, Fatal, System = int.MaxValue }

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
    public void System(ref DefaultInterpolatedStringHandler handler) => Log(LogLevel.System, ref handler);
}

public interface ILogger<out T> : ILogger;

public sealed class NullLogger<T> : ILogger<T>
{
    public static readonly NullLogger<T> Instance = new();
    public void Log(LogLevel level, ref DefaultInterpolatedStringHandler handler) => handler.Clear();
    public void Exception(Exception? e, string message) { }
    public void Drain() { }
}

public sealed class LoggerBase : ILogger
{
    private readonly LogLevel _minimumLogLevel;

    private readonly ConcurrentQueue<(LogLevel level, string value, Type? caller)> _messages = [];

    public LoggerBase(PackOptions options)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        _minimumLogLevel = options.LogLevel;
    }

    internal void Log(
        LogLevel level,
        ref DefaultInterpolatedStringHandler handler,
        Type caller)
    {
        if (level < _minimumLogLevel)
        {
            return;
        }

        if (level == LogLevel.System)
        {
            LogInternal(level, handler.ToStringAndClear());
        }
        else
        {
            _messages.Enqueue((level, handler.ToStringAndClear(), caller));
        }
    }

    void ILogger.Log(
        LogLevel level,
        ref DefaultInterpolatedStringHandler handler)
    {
        if (level < _minimumLogLevel)
        {
            return;
        }

        if (level == LogLevel.System)
        {
            lock (typeof(Console))
            {
                LogInternal(level, handler.ToStringAndClear());
            }
        }
        else
        {
            _messages.Enqueue((level, handler.ToStringAndClear(), null));
        }
    }

    public void Drain()
    {
        lock (typeof(Console))
        {
            foreach (var grouping in _messages.OrderBy(x => x.level).GroupBy(x => x.caller))
            {
                foreach (var (level, value, _) in grouping)
                    LogInternal(level, value);
            }
        }
    }

    private static void LogInternal(LogLevel level, string message)
    {
        var defaultForeground = Console.ForegroundColor;
        var defaultBackground = Console.BackgroundColor;

        TextWriter output = Console.Out;

        GetPrefix(
            level,
            out var prefix,
            out ConsoleColor prefixColor,
            out ConsoleColor? backgroundColor,
            out ConsoleColor? messageColor);

        if (!prefix.IsEmpty)
        {
            Console.BackgroundColor = backgroundColor ?? defaultBackground;
            output.Write(' ');
            Console.BackgroundColor = defaultBackground;

            Console.ForegroundColor = prefixColor;
            output.Write(prefix);
        }
        else
        {
            output.Write("        ");
        }

        Console.ForegroundColor = messageColor ?? defaultForeground;
        output.Write(message);

        output.Write(Environment.NewLine);
        Console.ForegroundColor = defaultForeground;
    }

    private static void GetPrefix(
        LogLevel level,
        out ReadOnlySpan<char> msg,
        out ConsoleColor prefixColor,
        out ConsoleColor? backgroundColor,
        out ConsoleColor? messageColor)
    {
        messageColor = null;

        switch (level)
        {
            case LogLevel.Debug:
                msg = " debug ";
                prefixColor = ConsoleColor.Gray;
                backgroundColor = ConsoleColor.DarkGray;
                break;
            case LogLevel.Info:
                msg = "  info ";
                prefixColor = ConsoleColor.Green;
                backgroundColor = ConsoleColor.DarkGreen;
                break;
            case LogLevel.Warn:
                msg = "  warn ";
                prefixColor = ConsoleColor.Yellow;
                backgroundColor = ConsoleColor.DarkYellow;
                break;
            case LogLevel.Error:
                msg = " error ";
                prefixColor = ConsoleColor.Red;
                backgroundColor = ConsoleColor.DarkRed;
                break;
            case LogLevel.Fatal:
                msg = " fatal ";
                prefixColor = ConsoleColor.Magenta;
                backgroundColor = ConsoleColor.DarkMagenta;
                break;
            case LogLevel.System:
                msg = default;
                prefixColor = default;
                messageColor = ConsoleColor.Cyan;
                backgroundColor = null;
                break;
            default:
                msg = "";
                prefixColor = default;
                backgroundColor = default;
                break;
        }
    }

    public void Exception(Exception? e, string message)
    {
        lock (typeof(Console))
        {
            if (e is null)
            {
                LogInternal(LogLevel.Fatal, $"{message}");
            }
            else
            {
                LogInternal(LogLevel.Fatal, $"{message}{Environment.NewLine}{Environment.NewLine}Exception:{Environment.NewLine}{e}");
            }
        }
    }
}

public sealed class Logger<T>(LoggerBase logger) : ILogger<T>
{
    public void Drain() => logger.Drain();
    public void Exception(Exception? e, string message) => logger.Exception(e, message);
    public void Log(LogLevel level, ref DefaultInterpolatedStringHandler handler) => logger.Log(level, ref handler, typeof(T));
}
