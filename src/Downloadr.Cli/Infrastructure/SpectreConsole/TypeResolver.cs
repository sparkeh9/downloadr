namespace Downloadr.Cli.Infrastructure.SpectreConsole;

using System;
using Spectre.Console.Cli;

public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider provider;

    public TypeResolver( IServiceProvider provider )
    {
        this.provider = provider;
    }

    public object? Resolve( Type? type )
    {
        return type == null ? null : provider.GetService( type );
    }

    public void Dispose()
    {
        if ( provider is IDisposable disposable )
        {
            disposable.Dispose();
        }
    }
}