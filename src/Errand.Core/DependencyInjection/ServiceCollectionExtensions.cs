using System.Reflection;
using System.Runtime.CompilerServices;
using Errand.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Errand.Core.DependencyInjection;

/// <summary>
/// Extension methods for registering Errand services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The fully-qualified name of the class that the Errand source generator emits.
    /// </summary>
    private const string GeneratedSenderTypeName = "Errand.Generated.ErrandSender";

    /// <summary>
    /// Registers Errand's generated <see cref="IErrandSender"/> implementation as a singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This overload locates the generated type by scanning the assembly of
    /// <typeparamref name="TAssemblyAnchor"/>. Use any type defined in the project that
    /// contains your <c>IErrandHandler</c> implementations — typically <c>Program</c> in an
    /// ASP.NET Core application.
    /// </para>
    /// <para>
    /// This overload is Native AOT-compatible because the generated type is preserved by the
    /// linker as long as it is referenced (which it is, by the generated source itself).
    /// </para>
    /// </remarks>
    /// <typeparam name="TAssemblyAnchor">
    /// Any type whose containing assembly holds the Errand-generated dispatcher.
    /// </typeparam>
    /// <param name="services">The service collection to register with.</param>
    /// <returns>The original <paramref name="services"/> to allow method chaining.</returns>
    /// <example>
    /// <code>
    /// // ASP.NET Core / Generic Host
    /// builder.Services.AddErrand&lt;Program&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddErrand<TAssemblyAnchor>(
        this IServiceCollection services)
    {
        return AddErrand(services, typeof(TAssemblyAnchor).Assembly);
    }

    /// <summary>
    /// Registers Errand's generated <see cref="IErrandSender"/> implementation as a singleton,
    /// locating the generated type in the specified <paramref name="assembly"/>.
    /// </summary>
    /// <remarks>
    /// Use this overload in integration tests or class-library scenarios where
    /// <see cref="Assembly.GetEntryAssembly()"/> may not point to the assembly that
    /// contains the generated dispatcher.
    /// </remarks>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="assembly">
    /// The assembly that was compiled with <c>Errand.SourceGenerator</c> and therefore
    /// contains <c>Errand.Generated.ErrandSender</c>.
    /// </param>
    /// <returns>The original <paramref name="services"/> to allow method chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="services"/> or <paramref name="assembly"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the generated sender type cannot be found in <paramref name="assembly"/>.
    /// This typically means <c>Errand.SourceGenerator</c> was not added to the project, or
    /// the project was not rebuilt after the handlers were defined.
    /// </exception>
    public static IServiceCollection AddErrand(
        this IServiceCollection services,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        var senderType = assembly.GetType(GeneratedSenderTypeName)
            ?? throw new InvalidOperationException(
                $"Type '{GeneratedSenderTypeName}' was not found in assembly " +
                $"'{assembly.GetName().Name}'. " +
                "Make sure Errand.SourceGenerator is referenced by the project that contains " +
                "your IErrandHandler implementations and that the project has been built.");

        // ServiceDescriptor.Singleton is defined in DI Abstractions — no full DI package needed.
        services.Add(ServiceDescriptor.Singleton(typeof(IErrandSender), senderType));
        return services;
    }

    /// <summary>
    /// Registers Errand's generated <see cref="IErrandSender"/> implementation as a singleton,
    /// locating the generated type in the calling assembly.
    /// </summary>
    /// <remarks>
    /// Suitable for simple application scenarios where <c>AddErrand()</c> is called directly
    /// from the assembly that was compiled with <c>Errand.SourceGenerator</c>.
    /// For test projects or multi-assembly setups, prefer
    /// <see cref="AddErrand{TAssemblyAnchor}"/> or <see cref="AddErrand(IServiceCollection,Assembly)"/>.
    /// </remarks>
    /// <param name="services">The service collection to register with.</param>
    /// <returns>The original <paramref name="services"/> to allow method chaining.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IServiceCollection AddErrand(this IServiceCollection services)
    {
        // NoInlining prevents the JIT from merging this stack frame with the caller's,
        // which ensures GetCallingAssembly() returns the user's assembly and not Errand.Core.
        return AddErrand(services, Assembly.GetCallingAssembly());
    }
}
