using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace RepoTodo.Commands;

internal sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly ServiceCollection _services = [];

    public void Register(Type service, Type implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _services.AddSingleton(service, _ => factory());
    }

    public ITypeResolver Build()
    {
        return new TypeResolver(_services.BuildServiceProvider());
    }

    private sealed class TypeResolver(IServiceProvider provider) : ITypeResolver
    {
        public object? Resolve(Type? type) => type is null ? null : provider.GetService(type);
    }
}
