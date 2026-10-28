using Azure.Identity;
using ClaimPartners.Users.Configuration;
using ClaimPartners.Users.Providers.Graph;
using ClaimPartners.Users.Tests.Shared;
using Moq;

namespace ClaimPartners.Users.Tests.Providers.Graph;

public sealed class GraphCredentialFactoryTests
{
    [Fact]
    public void Create_WithClientAssertionProvider_UsesClientAssertionsEvenWithASecret()
    {
        var credential = GraphCredentialFactory.Create(TestOptions.Create(), new Mock<IGraphClientAssertionProvider>().Object);

        Assert.IsType<ClientAssertionCredential>(credential);
    }

    [Fact]
    public void Create_WithoutClientAssertionProvider_UsesTheSecret()
    {
        var credential = GraphCredentialFactory.Create(TestOptions.Create());

        Assert.IsType<ClientSecretCredential>(credential);
    }

    [Fact]
    public async Task AssertionCallback_AsksForTheGraphClientIdAndTheTenantTokenEndpoint()
    {
        var options = TestOptions.Create();
        var assertionProvider = new Mock<IGraphClientAssertionProvider>();
        assertionProvider
            .Setup(provider => provider.GetAssertionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("signed-jwt");

        var assertion = await GraphCredentialFactory.AssertionCallback(options, assertionProvider.Object)(TestContext.Current.CancellationToken);

        Assert.Equal("signed-jwt", assertion);
        assertionProvider.Verify(provider => provider.GetAssertionAsync(
            options.GraphClientId,
            $"https://login.microsoftonline.com/{options.TenantId}/oauth2/v2.0/token",
            It.IsAny<CancellationToken>()));
    }
}
