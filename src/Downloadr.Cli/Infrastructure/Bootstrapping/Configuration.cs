namespace Downloadr.Cli.Infrastructure.Bootstrapping;

using System;
using System.IO;
using Microsoft.Extensions.Configuration;

public static class Configuration
{
    public static IConfigurationRoot BuildConfig()
    {
        return new ConfigurationBuilder().SetBasePath( Directory.GetCurrentDirectory() )
                                         .AddJsonFile( "appsettings.json", optional: true, reloadOnChange: true )
                                         .AddJsonFile(
                                              $"appsettings.{Environment.GetEnvironmentVariable( "DOTNET_ENVIRONMENT" )
                                                             ?? "Production"}.json",
                                              optional: true, reloadOnChange: true )
                                         .AddEnvironmentVariables( "TWITCH_HID_" )
                                         .AddUserSecrets( typeof( Program ).Assembly, optional: true )
                                         .Build();
    }
}