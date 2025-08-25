namespace Downloadr.Cli.Infrastructure.SpectreConsole;

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection services;

    public TypeRegistrar( IServiceCollection services )
    {
        this.services = services;
    }

    public ITypeResolver Build()
    {
        return new TypeResolver( services.BuildServiceProvider() );
    }

    public void Register( Type service, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementation )
    {
        services.AddSingleton( service, implementation );
    }

    public void RegisterInstance( Type service, object implementation )
    {
        services.AddSingleton( service, implementation );
    }

    public void RegisterLazy( Type service, Func<object> factory )
    {
        services.AddSingleton( service, _ => factory() );
    }
}