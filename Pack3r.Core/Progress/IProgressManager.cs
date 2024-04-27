namespace Pack3r.Progress;

public interface IProgressManager
{
    IProgressMeter Create(string name, int max);
}

public sealed class ConsoleProgressManager : IProgressManager
{
    public IProgressMeter Create(string name, int max) => new ConsoleProgressMeter(name, max);
}
