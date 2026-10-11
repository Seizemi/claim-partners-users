using ClaimPartners.Users.Core;
using ClaimPartners.Users.Domain;
using ClaimPartners.Users.Tests.Shared;
using Moq;

namespace ClaimPartners.Users.Tests.Core;

public sealed class AppRoleCatalogTests
{
    private readonly Mock<IPartnerUserProvider> provider = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Constructor_DoesNotReadTheRoles()
    {
        _ = new AppRoleCatalog(provider.Object);

        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_FirstCall_MapsValuesAndIdsBothWays()
    {
        provider.Setup(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TestOptions.AppRoles);

        var roles = await new AppRoleCatalog(provider.Object).GetAsync(Token);

        Assert.True(roles.TryGetId("Agent", out var agentId));
        Assert.Equal(TestOptions.AgentRoleId, agentId);
        Assert.False(roles.TryGetId("agent", out _));
        Assert.Equal("Supervisor", roles.ValueOf(TestOptions.SupervisorRoleId));
        Assert.Null(roles.ValueOf(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAsync_CalledTwice_ReadsGraphOnce()
    {
        provider.Setup(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TestOptions.AppRoles);
        var catalog = new AppRoleCatalog(provider.Object);

        var first = await catalog.GetAsync(Token);
        var second = await catalog.GetAsync(Token);

        Assert.Same(first, second);
        provider.Verify(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_ConcurrentFirstCalls_ReadGraphOnce()
    {
        var release = new TaskCompletionSource<IReadOnlyList<AppRoleDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Setup(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>())).Returns(release.Task);
        var catalog = new AppRoleCatalog(provider.Object);

        var calls = Enumerable.Range(0, 5).Select(_ => catalog.GetAsync(Token)).ToList();
        release.SetResult(TestOptions.AppRoles);
        var results = await Task.WhenAll(calls);

        Assert.All(results, roles => Assert.Same(results[0], roles));
        provider.Verify(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_AfterAFailedRead_TriesAgain()
    {
        provider.SetupSequence(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PartnerUserException(PartnerUserErrorCode.GraphError, "Graph is down."))
            .ReturnsAsync(TestOptions.AppRoles);
        var catalog = new AppRoleCatalog(provider.Object);

        await Assert.ThrowsAsync<PartnerUserException>(() => catalog.GetAsync(Token));
        var roles = await catalog.GetAsync(Token);

        Assert.True(roles.TryGetId("Agent", out _));
        provider.Verify(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetAsync_AfterACancelledRead_TriesAgain()
    {
        provider.SetupSequence(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException())
            .ReturnsAsync(TestOptions.AppRoles);
        var catalog = new AppRoleCatalog(provider.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => catalog.GetAsync(Token));
        var roles = await catalog.GetAsync(Token);

        Assert.True(roles.TryGetId("Supervisor", out _));
    }
}
