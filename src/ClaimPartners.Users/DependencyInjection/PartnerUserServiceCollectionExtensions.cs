using ClaimPartners.Users.Configuration;
using ClaimPartners.Users.Core;
using ClaimPartners.Users.Providers.Graph;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace ClaimPartners.Users.DependencyInjection;

/// <summary>
/// Registration of the partner user management services.
/// </summary>
public static class PartnerUserServiceCollectionExtensions
{
    private static readonly string[] GraphScopes = ["https://graph.microsoft.com/.default"];

    /// <summary>
    /// Registers <see cref="IPartnerUserService"/> with its options bound from the <c>PartnerUsers</c> section of
    /// <paramref name="configuration"/>. The options are validated when the host starts.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration (root), which contains the <c>PartnerUsers</c> section.</param>
    public static IServiceCollection AddPartnerUserManagement(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddPartnerUserManagementCore(configuration, clientAssertionProviderFactory: null);
    }

    /// <summary>
    /// Registers <see cref="IPartnerUserService"/> like <see cref="AddPartnerUserManagement(IServiceCollection, IConfiguration)"/>,
    /// the Graph app registration signing in with the client assertions of <paramref name="clientAssertionProviderFactory"/>.
    /// <c>GraphClientSecret</c> and <c>GraphCertificateThumbprint</c> must then be empty.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration (root), which contains the <c>PartnerUsers</c> section.</param>
    /// <param name="clientAssertionProviderFactory">Creates the provider of client assertions, once.</param>
    public static IServiceCollection AddPartnerUserManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, IGraphClientAssertionProvider> clientAssertionProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clientAssertionProviderFactory);

        return services.AddPartnerUserManagementCore(configuration, clientAssertionProviderFactory);
    }

    private static IServiceCollection AddPartnerUserManagementCore(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, IGraphClientAssertionProvider>? clientAssertionProviderFactory)
    {
        services.AddOptions<PartnerUserOptions>()
            .Bind(configuration.GetSection(PartnerUserOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PartnerUserOptions>>(
            new PartnerUserOptionsValidator(usesClientAssertion: clientAssertionProviderFactory is not null));

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PartnerUserOptions>>().Value;
            var credential = GraphCredentialFactory.Create(options, clientAssertionProviderFactory?.Invoke(provider));
            return new PartnerUsersGraphClient(new GraphServiceClient(credential, GraphScopes));
        });
        services.AddSingleton<IPartnerUserProvider, GraphPartnerUserProvider>();
        services.AddSingleton<AppRoleCatalog>();
        services.AddSingleton<IPartnerUserService, PartnerUserService>();

        return services;
    }
}
