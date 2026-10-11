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

        services.AddOptions<PartnerUserOptions>()
            .Bind(configuration.GetSection(PartnerUserOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PartnerUserOptions>, PartnerUserOptionsValidator>();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PartnerUserOptions>>().Value;
            return new PartnerUsersGraphClient(new GraphServiceClient(GraphCredentialFactory.Create(options), GraphScopes));
        });
        services.AddSingleton<IPartnerUserProvider, GraphPartnerUserProvider>();
        services.AddSingleton<AppRoleCatalog>();
        services.AddSingleton<IPartnerUserService, PartnerUserService>();

        return services;
    }
}
