using Pack3r.Models;
using Pack3r.Services;

namespace Pack3r.Tests;

public sealed class NoopChecker : IIntegrityChecker
{
    public void Log()
    {
    }

    ValueTask IIntegrityChecker.CheckIntegrity(IAsset asset)
    {
        throw new NotImplementedException();
    }
}
