using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Pack3r.Logging;

public sealed class Logger<T>(LoggerBase logger) : ILogger<T>
{
    private static readonly string _typeName = typeof(T).Name;

    public void Drain() => logger.Drain();
    public void Exception(Exception? e, string message) => logger.Exception(e, message);
    public void Log(LogLevel level, ref DefaultInterpolatedStringHandler handler) => logger.Log(level, ref handler, _typeName);
}

public sealed class LoggerBase : ILogger
{
    private readonly record struct LogMessage(
        LogLevel Level,
        string Message,
        string? Context)
        : IComparable<LogMessage>
    {
        public readonly long Timestamp = Stopwatch.GetTimestamp();

        public int CompareTo(LogMessage other)
        {
            int cmp = Level.CompareTo(other.Level);

            if (cmp == 0)
            {
                cmp = string.CompareOrdinal(Context, other.Context);
            }

            if (cmp == 0)
            {
                cmp = Timestamp.CompareTo(other.Timestamp);
            }

            return cmp;
        }
    }

    private readonly LogLevel _minimumLogLevel;
    private readonly ConcurrentBag<LogMessage> _messages = [];

    public LoggerBase(PackOptions options)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        _minimumLogLevel = options.LogLevel;
    }

    internal void Log(
        LogLevel level,
        ref DefaultInterpolatedStringHandler handler,
        string? caller)
    {
        if (level != LogLevel.Fatal && level < _minimumLogLevel)
        {
            return;
        }

        if (level == (LogLevel)byte.MaxValue)
        {
            lock (Global.ConsoleLock)
            {
                LogInternalNoLock(level, handler.ToStringAndClear(), caller);
            }
        }
        else
        {
            _messages.Add(new(level, handler.ToStringAndClear(), caller));
        }
    }

    void ILogger.Log(
        LogLevel level,
        ref DefaultInterpolatedStringHandler handler)
    {
        Log(level, ref handler, null);
    }

    public void Drain()
    {
        Span<LogMessage> messages = _messages.ToArray();
        messages.Sort();

        _messages.Clear();

        lock (Global.ConsoleLock)
        {
            foreach (ref var message in messages)
            {
                LogInternalNoLock(message.Level, message.Message, message.Context);
            }
        }
    }

    private void LogInternalNoLock(LogLevel level, string message, string? context)
    {
        if (_minimumLogLevel == LogLevel.None)
        {
            Debug.Assert(level is LogLevel.Fatal, $"Invalid None loglevel got through: {level}");
            Console.Out.WriteLine(message);
            return;
        }

        TextWriter output = Console.Out;

        GetPrefix(
            level,
            out var prefix,
            out ConsoleColor prefixColor,
            out ConsoleColor? backgroundColor,
            out ConsoleColor? messageColor);

        if (!prefix.IsEmpty)
        {

            if (backgroundColor.HasValue)
            {
                Console.BackgroundColor = backgroundColor.Value;
            }

            output.Write(' ');
            Console.ResetColor();
            Console.ForegroundColor = prefixColor;
            output.Write(prefix);
        }
        else
        {
            output.Write("        ");
        }

        if (!string.IsNullOrEmpty(context))
        {
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            output.Write('[');
            output.Write(context);
            output.Write("] ");
            Console.ResetColor();
        }

        if (messageColor.HasValue)
        {
            Console.ForegroundColor = messageColor.Value;
        }
        output.Write(message);
        output.Write(Environment.NewLine);
        Console.ResetColor();
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
            case (LogLevel)byte.MaxValue:
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
        lock (Global.ConsoleLock)
        {
            LogInternalNoLock(
                LogLevel.Fatal,
                e is null
                    ? $"{message}"
                    : $"{message}{Environment.NewLine}{Environment.NewLine}Exception:{Environment.NewLine}{e}",
                null);
        }
    }
}
