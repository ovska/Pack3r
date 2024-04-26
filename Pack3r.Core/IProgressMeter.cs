using System.Diagnostics;

namespace Pack3r;

public interface IProgressManager
{
    IProgressMeter Create(string name, int max);
}

public interface IProgressMeter : IDisposable
{
    public void Report(int value);
}

public sealed class ConsoleProgressManager : IProgressManager
{
    public IProgressMeter Create(string name, int max) => new ConsoleProgressMeter(name, max);
}

public sealed class ConsoleProgressMeter : IProgressMeter
{
    private readonly string _name;
    private readonly int _max;
    private long _lastPrint;
    private uint _lastSpin;

    private static readonly char[] _spinner = ['-', '\\', '|', '/'];

    public ConsoleProgressMeter(string name, int max)
    {
        _name = name;
        _max = max;
        Report(0);
    }

    public void Report(int value)
    {
        if (value < _max &&
            Stopwatch.GetElapsedTime(_lastPrint) < TimeSpan.FromMilliseconds(33))
        {
            return;
        }

        lock (typeof(Console))
        {
            _lastPrint = Stopwatch.GetTimestamp();

            if (value != 0)
            {
                Console.Out.Write('\r');
            }

            var foreground = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Out.Write("      ");

            if (value >= _max)
            {
                Console.Out.Write("  ");
            }
            else
            {
                Console.Out.Write(_spinner[(_lastSpin++) % _spinner.Length]);
                Console.Out.Write(" ");
            }

            Console.ForegroundColor = foreground;

            Console.Out.Write(_name);
            Console.Out.Write(' ');
            Console.Out.Write(value);
            Console.Out.Write(" / ");
            Console.Out.Write(_max);

            if (value >= _max)
            {
                Console.Out.Write(' ');
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Out.Write("DONE");
                Console.ForegroundColor = foreground;
            }
        }
    }

    public void Dispose()
    {
        lock (typeof(Console))
        {
            Console.WriteLine();
        }
    }
}
