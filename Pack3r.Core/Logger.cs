using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Pack3r.Extensions;

namespace Pack3r;

public enum LogLevel { Debug, Info, Warn, Error, Fatal, System = int.MaxValue }

public interface ILogger
{
    void Log(LogLevel level, ref DefaultInterpolatedStringHandler handler);
    void Exception(Exception? e, string message);

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
}

public sealed class Logger<T> : ILogger<T>, IAsyncDisposable
{
    private readonly LogLevel _minimumLogLevel;
    private readonly object _lock = new();

    public Logger(PackOptions options)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        _minimumLogLevel = options.LogLevel;
    }

    public void Log(
        LogLevel level,
        ref DefaultInterpolatedStringHandler handler)
    {
        if (level < _minimumLogLevel)
        {
            //return;
        }

        bool lockTaken = false;
        Monitor.Enter(_lock, ref lockTaken);

        try
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

            Console.BackgroundColor = backgroundColor ?? defaultBackground;
            output.Write(' ');
            Console.BackgroundColor = defaultBackground;

            Console.ForegroundColor = prefixColor;
            output.Write(prefix);

            Console.ForegroundColor = ConsoleColor.White;
            output.Write('[');

            var time = DateTime.Now;
            Span<char> buffer = stackalloc char[16];
            bool success = time.TryFormat(buffer, out int written, "HH:mm:ss", CultureInfo.InvariantCulture);
            System.Diagnostics.Debug.Assert(success);

            output.Write(buffer[..written]);

            output.Write(']');

            Console.ForegroundColor = messageColor ?? defaultForeground;
            output.Write(' ');
            output.Write(handler.GetInternalBuffer());

            output.Write(Environment.NewLine);
            
            Console.ForegroundColor = defaultForeground;
        }
        finally
        {
            if (lockTaken)
                Monitor.Exit(_lock);

            handler.Clear();
        }
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
                prefixColor = ConsoleColor.Cyan;
                backgroundColor = ConsoleColor.DarkCyan;
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
                msg = "       ";
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
        if (e is null)
            Log(LogLevel.Fatal, $"{message}");
        else
            Log(LogLevel.Fatal, $"{message}{Environment.NewLine}{Environment.NewLine}Exception:{Environment.NewLine}{e}");
    }

    public ValueTask DisposeAsync()
    {
        throw new NotImplementedException();
    }
}
