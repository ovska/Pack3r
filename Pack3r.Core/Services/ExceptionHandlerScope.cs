using Microsoft.Extensions.Logging;

namespace Pack3r.Services;

public interface IExceptionHandlerScope : IDisposable
{
}

public sealed class ExceptionHandlerScope : IExceptionHandlerScope
{
    private readonly ILogger<ExceptionHandlerScope> _logger;
    private readonly CancellationToken _cancellationToken;

    public ExceptionHandlerScope(
        ILogger<ExceptionHandlerScope> logger,
        CancellationToken cancellationToken)
    {
        _logger = logger;
        _cancellationToken = cancellationToken;

        AppDomain.CurrentDomain.UnhandledException += HandleException;
    }

    private void HandleException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject;

        if (ex is OperationCanceledException && _cancellationToken.IsCancellationRequested)
        {
            _logger.LogCritical("Operation was canceled by the user");
            return;
        }
        else if (ex is DirectoryNotFoundException dnfe)
        {
            _logger.LogCritical(dnfe, "Error, directory not found, aborting");
        }
        else if (ex is FileNotFoundException fnfe)
        {
            _logger.LogCritical(fnfe, "Error, file {path} not found, aborting", fnfe.FileName);
        }
        else
        {
            _logger.LogCritical(ex as Exception, "Unhandled exception, aborting");
        }

        Console.WriteLine("Press Enter to exit");
        Console.ReadLine();
        Environment.Exit(1);
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= HandleException;
    }
}
