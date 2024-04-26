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

        AppDomain.CurrentDomain.UnhandledException += HandleException;
    }

    private void HandleException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;

        if (ex is ControlledException)
        {
        }
        else if (ex is OperationCanceledException && _cts.IsCancellationRequested)
        {
            _logger.Exception(null, "Operation was canceled by the user");
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

        Console.WriteLine("Press Enter to exit");
        Console.ReadLine();
        Environment.Exit(1);
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= HandleException;
        _cts.Dispose();
    }
}
