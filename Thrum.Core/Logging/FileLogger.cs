using Microsoft.Extensions.Logging;

namespace Thrum.Core.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly object _lock = new();

    public FileLoggerProvider(string? customLogDir = null)
    {
        _logDirectory = customLogDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Thrum", "logs");

        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _logDirectory, _lock);

    public void Dispose() { }
}

public sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _logDirectory;
    private readonly object _lock;

    public FileLogger(string categoryName, string logDirectory, object lockObj)
    {
        _categoryName = categoryName;
        _logDirectory = logDirectory;
        _lock = lockObj;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string message = formatter(state, exception);
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string logLine = $"[{timestamp}] [{logLevel}] [{_categoryName}] {message}";

        if (exception != null)
        {
            logLine += Environment.NewLine + exception;
        }

        lock (_lock)
        {
            try
            {
                string dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
                string logFile = Path.Combine(_logDirectory, $"thrum_{dateStr}.log");
                File.AppendAllText(logFile, logLine + Environment.NewLine);
            }
            catch
            {
                // Never crash the application on logging failure
            }
        }
    }
}
