using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.IO;
using System.Runtime.CompilerServices;
using WpfApp.Services.Models;

namespace WpfApp.Services.Utils;

public class SerilogManager : ISerilogManager, ILogger, IDisposable
{
    private ILogger? _logger;
    private string _baseDirectory = string.Empty;
    private bool _disposed;
    private bool _initialized;

    public void Initialize(DebugConfig debugConfig)
    {
        if (_initialized) return;

        try
        {
            if (!debugConfig.IsDebugMode) return;

            var logLevel = debugConfig.LogLevel.ToLower() switch
            {
                "debug" => LogEventLevel.Debug,
                "information" => LogEventLevel.Information,
                "warning" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error,
                _ => LogEventLevel.Information
            };

            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Is(logLevel)
                .Enrich.WithThreadId()
                .Enrich.FromLogContext();

            const string outputTemplate =
                "[{Timestamp:HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}.{CallerMember}:{LineNumber}] {Message}{NewLine}{Exception}";

            loggerConfig = loggerConfig.WriteTo.Debug(outputTemplate: outputTemplate);

            if (!string.IsNullOrEmpty(_baseDirectory))
            {
                var logPath = Path.Combine(_baseDirectory, "logs", "app.log");
                var logDir = Path.GetDirectoryName(logPath);

                if (!string.IsNullOrEmpty(logDir))
                    Directory.CreateDirectory(logDir);

                if (debugConfig.FileSettings.RetainDays > 0 && Directory.Exists(logDir))
                {
                    var cutoff = DateTime.Now.AddDays(-debugConfig.FileSettings.RetainDays);
                    foreach (var file in Directory.GetFiles(logDir, "app*.log"))
                        if (File.GetLastWriteTime(file) < cutoff)
                            try { File.Delete(file); } catch { }
                }

                loggerConfig = loggerConfig.WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: debugConfig.FileSettings.MaxFileCount,
                    fileSizeLimitBytes: debugConfig.FileSettings.MaxFileSize * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    outputTemplate: outputTemplate);
            }

            _logger = loggerConfig.CreateLogger();
            _initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"初始化日志系统失败: {ex}");
            throw;
        }
    }

    public void SetBaseDirectory(string path)
    {
        _baseDirectory = path;
    }

    public void UpdateLoggerConfig(DebugConfig debugConfig)
    {
        if (_disposed) return;
        Initialize(debugConfig);
    }

    #region ILogger Implementation

    public void Write(LogEvent logEvent)
    {
        if (_disposed || _logger == null) return;
        _logger.Write(logEvent);
    }

    public bool BindMessageTemplate(string? messageTemplate, object?[]? propertyValues,
        out MessageTemplate? parsedTemplate, out IEnumerable<LogEventProperty>? boundProperties)
    {
        if (_logger is ILogger concreteLogger)
            return concreteLogger.BindMessageTemplate(messageTemplate, propertyValues, out parsedTemplate,
                out boundProperties);

        parsedTemplate = null;
        boundProperties = Array.Empty<LogEventProperty>();
        return false;
    }
    
    public bool BindProperty(string? propertyName, object? value, bool destructureObjects,
        out LogEventProperty? property)
    {
        if (_logger is ILogger concreteLogger)
            return concreteLogger.BindProperty(propertyName, value, destructureObjects, out property);

        property = null;
        return false;
    }

    #endregion

    #region Logging Methods

    public void Debug(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (_disposed || _logger == null) return;
        _logger
            .ForContext("CallerMember", memberName)
            .ForContext("SourceContext", Path.GetFileNameWithoutExtension(sourceFilePath))
            .ForContext("LineNumber", sourceLineNumber)
            .Debug(message);
    }

    public void Info(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (_disposed || _logger == null) return;
        _logger
            .ForContext("CallerMember", memberName)
            .ForContext("SourceContext", Path.GetFileNameWithoutExtension(sourceFilePath))
            .ForContext("LineNumber", sourceLineNumber)
            .Information(message);
    }

    public void Warning(string message,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (_disposed || _logger == null) return;
        _logger
            .ForContext("CallerMember", memberName)
            .ForContext("SourceContext", Path.GetFileNameWithoutExtension(sourceFilePath))
            .ForContext("LineNumber", sourceLineNumber)
            .Warning(message);
    }

    public void Error(string message, Exception? ex = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (_disposed || _logger == null) return;
        _logger
            .ForContext("CallerMember", memberName)
            .ForContext("SourceContext", Path.GetFileNameWithoutExtension(sourceFilePath))
            .ForContext("LineNumber", sourceLineNumber)
            .Error(ex, message);
    }

    public void SequenceEvent(string message, string? details = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (_disposed || _logger == null) return;
        _logger
            .ForContext("CallerMember", memberName)
            .ForContext("SourceContext", Path.GetFileNameWithoutExtension(sourceFilePath))
            .ForContext("LineNumber", sourceLineNumber)
            .Information(details == null ? $"[Sequence] {message}" : $"[Sequence] {message} - {details}");
    }

    public void DriverEvent(string message, string? details = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (_disposed || _logger == null) return;
        _logger
            .ForContext("CallerMember", memberName)
            .ForContext("SourceContext", Path.GetFileNameWithoutExtension(sourceFilePath))
            .ForContext("LineNumber", sourceLineNumber)
            .Information(details == null ? $"[Driver] {message}" : $"[Driver] {message} - {details}");
    }

    public void InitLog(string message, string? details = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        if (_disposed || _logger == null) return;
        _logger
            .ForContext("CallerMember", memberName)
            .ForContext("SourceContext", Path.GetFileNameWithoutExtension(sourceFilePath))
            .ForContext("LineNumber", sourceLineNumber)
            .Information(details == null ? $"[Init] {message}" : $"[Init] {message} - {details}");
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            if (_logger is IDisposable disposableLogger) disposableLogger.Dispose();
            _logger = null;
        }

        _disposed = true;
    }

    #endregion
}