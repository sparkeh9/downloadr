namespace Downloadr.Cli.Infrastructure.Bootstrapping;

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Settings.Configuration;

public static class Observability
{
    /// <summary>
    /// Creates a logger using configuration from appsettings.json.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>A configured logger instance.</returns>
    public static ILogger CreateLogger( IConfiguration configuration )
    {
        try
        {
            Directory.CreateDirectory( "logs" );

            var readerOptions = new ConfigurationReaderOptions(
                typeof(ConsoleLoggerConfigurationExtensions).Assembly,
                typeof(FileLoggerConfigurationExtensions).Assembly
            );

            // Create logger from configuration
            var logger = new LoggerConfiguration()
                         .ReadFrom
                         .Configuration( configuration, readerOptions )
                         .Enrich.FromLogContext()
                         .CreateLogger();

            logger.Information( "Logger initialised using configuration" );
            return logger;
        }
        catch ( Exception ex )
        {
            var fallbackLogger = CreateDefaultLogger();
            fallbackLogger.Error( ex, "Failed to create logger from configuration. Using fallback logger." );
            return CreateDefaultLogger();
        }
    }

    /// <summary>
    /// Creates a default logger when no configuration is available.
    /// This is kept for backwards compatibility.
    /// </summary>
    /// <returns>A default logger instance.</returns>
    private static ILogger CreateDefaultLogger()
    {
        return new LoggerConfiguration()
               .MinimumLevel.Information()
               .WriteTo.Console(
                   outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}" )
               .WriteTo.File( "logs/dataqi.log",
                              rollingInterval: RollingInterval.Day,
                              outputTemplate:
                              "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}" )
               .CreateLogger();
    }
}