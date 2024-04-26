using System.Diagnostics;
using Pack3r.Extensions;

namespace Pack3r.Services;

public sealed class AppLifetime : IDisposable
{
    public CancellationToken CancellationToken => _cts.Token;

    private readonly ILogger<AppLifetime> _logger;
    private readonly CancellationTokenSource _cts;

    public AppLifetime(ILogger<AppLifetime> logger)
    {
        _logger = logger;
        _cts = new CancellationTokenSource();

        Console.CancelKeyPress += CancelToken;
    }

    private void CancelToken(object? sender, ConsoleCancelEventArgs e)
    {
        _cts.Cancel();
        e.Cancel = true;
    }

    public void HandleException(Exception? ex)
    {
        bool dontDrain = false;

        if (ex is OperationCanceledException && _cts.IsCancellationRequested)
        {
            _logger.Exception(null, "Operation was canceled by the user");
            dontDrain = true;
        }
        else if (ex is ControlledException)
        {
        }
        else if (ex is EnvironmentException or InvalidDataException)
        {
            _logger.Exception(null, ex.Message);
        }
        else if (ex is DirectoryNotFoundException dnfe)
        {
            _logger.Exception(dnfe, "Error, file/directory not found, aborting");
        }
        else if (ex is FileNotFoundException fnfe)
        {
            _logger.Exception(fnfe, $"Error, file {fnfe.FileName} not found, aborting");
        }
        else if (ex is UnreachableException ure)
        {
            _logger.Exception(ure, "Internal error, aborting. Please report to developer");
        }
        else
        {
            _logger.Exception(ex, "Unhandled exception, aborting");
        }

        if (!dontDrain)
            _logger.Drain();

        _logger.System($"Press Enter to exit");
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= CancelToken;
        _cts.Dispose();
    }
}
