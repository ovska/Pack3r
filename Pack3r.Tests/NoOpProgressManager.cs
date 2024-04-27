using Pack3r.Progress;

namespace Pack3r.Tests;

public sealed class NoOpProgressMeter : IProgressMeter
{
    public void Dispose()
    {
    }

    public void Report(int value)
    {
    }
}

public sealed class NoOpProgressManager : IProgressManager
{
    public IProgressMeter Create(string name, int max) => new NoOpProgressMeter();
}
