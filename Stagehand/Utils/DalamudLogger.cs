using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stagehand.Utils;

/// <summary>
/// Implements an <see cref="ILogger"/> that logs messages using the Dalamud plugin logging system (<see cref="IPluginLog"/>).
/// </summary>
internal class DalamudLogger : ILogger
{
    readonly string _categoryName;
    readonly IPluginLog _pluginLog;
    readonly StagehandConfiguration _configuration;

    public DalamudLogger(string name, StagehandConfiguration configuration, IPluginLog pluginLog)
    {
        _categoryName = name;
        _pluginLog = pluginLog;
        _configuration = configuration;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // We'll let dalamud handle log level filtering for now
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = $"[{_categoryName}] {formatter(state, exception)}";

        if (exception != null)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"{message} {exception.Message}");
            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                builder.AppendLine(exception.StackTrace);
            }

            var innerException = exception.InnerException;
            while (innerException != null)
            {
                builder.AppendLine($"InnerException {innerException}: {innerException.Message}");

                if (!string.IsNullOrWhiteSpace(innerException.StackTrace))
                {
                    builder.AppendLine(innerException.StackTrace);
                }

                innerException = innerException.InnerException;
            }

            message = builder.ToString();
        }

        switch (logLevel)
        {
            case LogLevel.Trace:
                _pluginLog.Verbose(message);
                break;
            case LogLevel.Debug:
                _pluginLog.Debug(message);
                break;
            case LogLevel.Information:
                _pluginLog.Information(message);
                break;
            case LogLevel.Warning:
                _pluginLog.Warning(message);
                break;
            case LogLevel.Error:
            default: // Invalid log messages are a programming error
                _pluginLog.Error(message);
                break;
            case LogLevel.Critical:
                _pluginLog.Fatal(message);
                break;
        }
    }
}

[ProviderAlias("Dalamud")]
internal class DalamudLoggerProvider : ILoggerProvider
{
    readonly IPluginLog _pluginLog;
    readonly StagehandConfiguration _configuration;

    readonly ConcurrentDictionary<string, DalamudLogger> _categoryLoggers = new ConcurrentDictionary<string, DalamudLogger>();

    public DalamudLoggerProvider(IPluginLog pluginLog, StagehandConfiguration configuration)
    {
        _pluginLog = pluginLog;
        _configuration = configuration;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _categoryLoggers.GetOrAdd(categoryName, category => new DalamudLogger(category, _configuration, _pluginLog));
    }

    public void Dispose()
    {
        _categoryLoggers.Clear();
        GC.SuppressFinalize(this);
    }
}

internal static class DalamudLoggingExtensions
{
    public static ILoggingBuilder AddDalamudLogging(this ILoggingBuilder builder, IPluginLog pluginLog)
    {
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DalamudLoggerProvider>
            (b => new DalamudLoggerProvider(pluginLog, b.GetRequiredService<StagehandConfiguration>())));
        return builder;
    }
}
