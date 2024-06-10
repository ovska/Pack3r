using System.Diagnostics;

namespace Pack3r.Progress;

public interface IProgressMeter : IDisposable
{
    public void Report(int value);
}

public sealed class NoOpProgressMeter : IProgressMeter
{
    public void Dispose() { }
    public void Report(int value) { }
}

public sealed class ConsoleProgressMeter : IProgressMeter
{
    private readonly string _name;
    private readonly int? _max;
    private long _lastPrint;
    private uint _lastSpin;

    private readonly long _timestamp = Stopwatch.GetTimestamp();

    private static readonly char[] _spinner = ['-', '\\', '|', '/'];
    private static readonly TimeSpan _frequency = TimeSpan.FromMilliseconds(100);

    private char Spin => _spinner[_lastSpin++ % _spinner.Length];

    public ConsoleProgressMeter(string name, int? max)
    {
        _name = name;
        _max = max;
        Report(0);
    }

    public void Report(int value)
    {
        Debug.Assert(_max is null || value <= _max, $"Invalid value: {value} vs max {_max}");

        if (value < _max &&
            Stopwatch.GetElapsedTime(_lastPrint) < _frequency)
        {
            return;
        }

        _lastPrint = Stopwatch.GetTimestamp();

        lock (Global.ConsoleLock)
        {
            if (value != 0)
            {
                Console.Out.Write('\r');
            }

            var foreground = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.Gray;

            if (value >= _max)
            {
                Console.Out.Write("        ");
            }
            else
            {
                Console.Out.Write([' ', ' ', ' ', ' ', ' ', ' ', Spin, ' ']);
            }

            Console.ForegroundColor = foreground;

            Console.Out.Write(_name);
            Console.Out.Write(' ');
            Console.Out.Write(value);

            if (_max.HasValue)
            {
                Console.Out.Write(" / ");
                Console.Out.Write(_max.Value);
            }
        }
    }

    public void Dispose()
    {
        lock (Global.ConsoleLock)
        {
            var foreground = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Out.Write(" (");
            Console.Out.Write((int)Stopwatch.GetElapsedTime(_timestamp).TotalMilliseconds);
            Console.Out.Write("ms)");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Out.Write("\r   DONE ");
            Console.ForegroundColor = foreground;
            Console.WriteLine();
        }
    }
}
