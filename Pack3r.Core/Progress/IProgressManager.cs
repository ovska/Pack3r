namespace Pack3r.Progress;

public interface IProgressManager
{
    IProgressMeter Create(string name, int? max);
}

public sealed class ConsoleProgressManager(PackOptions options) : IProgressManager
{
    public IProgressMeter Create(string name, int? max) =>
        options.LogLevel == Logging.LogLevel.None
            ? new NoOpProgressMeter()
            : new ConsoleProgressMeter(name, max);
}
