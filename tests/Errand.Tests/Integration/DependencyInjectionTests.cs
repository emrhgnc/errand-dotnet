using Errand.Abstractions;
using Errand.Core.DependencyInjection;
using Errand.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Errand.Tests.Integration;

/// <summary>
/// Tests that <c>AddErrand</c> extension-method overloads correctly register
/// <see cref="IErrandSender"/> with an <see cref="IServiceCollection"/>.
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddErrand_TypeAnchorOverload_RegistersIErrandSender()
    {
        var services = new ServiceCollection();
        services.AddErrand<EchoHandler>();

        services.BuildServiceProvider()
            .GetRequiredService<IErrandSender>()
            .Should().NotBeNull();
    }

    [Fact]
    public void AddErrand_AssemblyOverload_RegistersIErrandSender()
    {
        var services = new ServiceCollection();
        services.AddErrand(typeof(EchoHandler).Assembly);

        services.BuildServiceProvider()
            .GetRequiredService<IErrandSender>()
            .Should().NotBeNull();
    }

    [Fact]
    public void AddErrand_ReturnsServiceCollection_ForMethodChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddErrand<EchoHandler>();

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void AddErrand_RegistersAsSingleton_SameInstanceResolvedTwice()
    {
        var provider = new ServiceCollection()
            .AddErrand<EchoHandler>()
            .BuildServiceProvider();

        var first  = provider.GetRequiredService<IErrandSender>();
        var second = provider.GetRequiredService<IErrandSender>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public async Task ResolvedSender_CanDispatch_EchoRequest()
    {
        var sender = new ServiceCollection()
            .AddErrand<EchoHandler>()
            .BuildServiceProvider()
            .GetRequiredService<IErrandSender>();

        var result = await sender.SendAsync(new EchoRequest { Message = "world" });

        result.Should().Be("world");
    }

    [Fact]
    public void AddErrand_WrongAssembly_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        // System.Runtime assembly has no Errand-generated sender.
        var act = () => services.AddErrand(typeof(object).Assembly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Errand.Generated.ErrandSender*");
    }

    [Fact]
    public void AddErrand_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;

        var act = () => services!.AddErrand<EchoHandler>();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddErrand_NullAssembly_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddErrand(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
