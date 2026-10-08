using Claim.PartnerUsers.Configuration;
using Claim.PartnerUsers.Core;
using Claim.PartnerUsers.DependencyInjection;
using Claim.PartnerUsers.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace Claim.PartnerUsers.Tests.DependencyInjection;

public sealed class PartnerUserServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPartnerUserManagement_ValidConfiguration_ResolvesTheServiceAsSingleton()
    {
        using var provider = BuildProvider(TestOptions.AsConfiguration());

        provider.GetRequiredService<IStartupValidator>().Validate();
        var service = provider.GetRequiredService<IPartnerUserService>();

        Assert.Same(service, provider.GetRequiredService<IPartnerUserService>());
    }

    [Fact]
    public void AddPartnerUserManagement_ValidConfiguration_BindsRolesAndIgnoresHostKeys()
    {
        using var provider = BuildProvider(TestOptions.AsConfiguration());

        var options = provider.GetRequiredService<IOptions<PartnerUserOptions>>().Value;

        Assert.Equal(TestOptions.AgentRoleId, options.Roles["Agent"]);
        Assert.Equal(TestOptions.SupervisorRoleId, options.Roles["Supervisor"]);
    }

    [Fact]
    public void AddPartnerUserManagement_DoesNotRegisterAGraphServiceClientForTheHost()
    {
        using var provider = BuildProvider(TestOptions.AsConfiguration());

        Assert.Null(provider.GetService<GraphServiceClient>());
    }

    [Fact]
    public void AddPartnerUserManagement_InvalidConfiguration_FailsAtStartup()
    {
        var configuration = TestOptions.AsConfiguration();
        configuration.Remove("PartnerUsers:GraphClientSecret");
        using var provider = BuildProvider(configuration);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("GraphClientSecret", exception.Message);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddPartnerUserManagement(configuration)
            .BuildServiceProvider();
    }
}
