using ClaimPartners.Users.Core;
using ClaimPartners.Users.Domain;
using ClaimPartners.Users.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;

namespace ClaimPartners.Users.Tests.Core;

public sealed class PartnerUserServiceTests
{
    private readonly Mock<IPartnerUserProvider> provider = new();
    private readonly FakeLogger<PartnerUserService> logger = new();
    private readonly string userId = Guid.NewGuid().ToString();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public PartnerUserServiceTests()
    {
        provider.Setup(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TestOptions.AppRoles);
    }

    private PartnerUserService CreateSut() => new(provider.Object, new AppRoleCatalog(provider.Object), logger);

    private static CreatePartnerUserRequest AgentRequest => new("jane@example.com", "Jane", "Agent");

    private static UserRecord NewUserRecord(string displayName = "Jane") =>
        new(Guid.NewGuid().ToString(), displayName, $"{displayName.ToLowerInvariant()}@example.com");

    // ---- CreateUserAsync ----

    [Theory]
    [InlineData("Administrator")]
    [InlineData("agent")]
    public async Task CreateUserAsync_RoleNotDefinedOnTheApp_ThrowsUnknownRoleWithoutCreatingAnything(string role)
    {
        var request = new CreatePartnerUserRequest("jane@example.com", "Jane", role);

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().CreateUserAsync(request, Token));

        Assert.Equal(PartnerUserErrorCode.UnknownRole, exception.Code);
        provider.Verify(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()), Times.Once);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateUserAsync_RolesCannotBeRead_ThrowsGraphErrorWithoutCreatingAnything()
    {
        provider.Setup(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PartnerUserException(PartnerUserErrorCode.GraphError, "Graph is down."));

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().CreateUserAsync(AgentRequest, Token));

        Assert.Equal(PartnerUserErrorCode.GraphError, exception.Code);
        provider.Verify(p => p.CreateUserAsync(It.IsAny<NewUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateUserAsync_EmailAlreadyExists_ThrowsUserAlreadyExistsWithoutCreating()
    {
        provider.Setup(p => p.EmailExistsAsync("jane@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().CreateUserAsync(AgentRequest, Token));

        Assert.Equal(PartnerUserErrorCode.UserAlreadyExists, exception.Code);
        Assert.DoesNotContain("jane@example.com", exception.Message);
        provider.Verify(p => p.CreateUserAsync(It.IsAny<NewUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateUserAsync_ValidRequest_CreatesUserWithoutRoleAndReturnsIt()
    {
        NewUser? created = null;
        provider.Setup(p => p.CreateUserAsync(It.IsAny<NewUser>(), It.IsAny<CancellationToken>()))
            .Callback<NewUser, CancellationToken>((user, _) => created = user)
            .ReturnsAsync(userId);

        var result = await CreateSut().CreateUserAsync(new CreatePartnerUserRequest("  jane@example.com ", " Jane Doe ", "Agent"), Token);

        Assert.Equal(new PartnerUser(userId, "Jane Doe", "jane@example.com", null), result);
        Assert.NotNull(created);
        Assert.Equal("jane@example.com", created.Email);
        Assert.Equal("Jane Doe", created.DisplayName);
        Assert.Equal(PasswordGenerator.Length, created.Password.Length);
        provider.Verify(p => p.AssignRoleAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateUserAsync_ValidRequest_LogsMaskedEmailAndNoPassword()
    {
        string? password = null;
        provider.Setup(p => p.CreateUserAsync(It.IsAny<NewUser>(), It.IsAny<CancellationToken>()))
            .Callback<NewUser, CancellationToken>((user, _) => password = user.Password)
            .ReturnsAsync(userId);

        await CreateSut().CreateUserAsync(AgentRequest, Token);

        var entry = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Contains("j***@example.com", entry.Message);
        Assert.Contains(userId, entry.Message);
        Assert.DoesNotContain("jane@example.com", entry.Message);
        Assert.DoesNotContain(password!, entry.Message);
    }

    // ---- AssignRoleAsync ----

    [Fact]
    public async Task AssignRoleAsync_KnownRole_AssignsItsIdAndLogs()
    {
        await CreateSut().AssignRoleAsync(userId, "Agent", Token);

        provider.Verify(p => p.AssignRoleAsync(userId, TestOptions.AgentRoleId, It.IsAny<CancellationToken>()), Times.Once);
        var entry = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Contains(userId, entry.Message);
        Assert.Contains("Agent", entry.Message);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("agent")]
    public async Task AssignRoleAsync_RoleNotDefinedOnTheApp_ThrowsUnknownRole(string role)
    {
        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().AssignRoleAsync(userId, role, Token));

        Assert.Equal(PartnerUserErrorCode.UnknownRole, exception.Code);
        provider.Verify(p => p.AssignRoleAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AssignRoleAsync_IdIsNotAGuid_ThrowsUserNotFoundWithoutCallingGraph()
    {
        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().AssignRoleAsync("../me", "Agent", Token));

        Assert.Equal(PartnerUserErrorCode.UserNotFound, exception.Code);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AssignRoleAsync_GraphFails_ThrowsTheProviderError()
    {
        var error = new PartnerUserException(PartnerUserErrorCode.GraphError, "Not replicated yet.");
        provider.Setup(p => p.AssignRoleAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(error);

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().AssignRoleAsync(userId, "Agent", Token));

        Assert.Same(error, exception);
    }

    // ---- ListUsersAsync ----

    [Fact]
    public async Task ListUsersAsync_NoAssignment_ReturnsEmptyWithoutLoadingUsers()
    {
        provider.Setup(p => p.ListRoleAssignmentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await CreateSut().ListUsersAsync(Token);

        Assert.Empty(result);
        provider.Verify(p => p.GetUsersAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        provider.Verify(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListUsersAsync_AssignedUsers_ReturnsThemWithRoleNamesSortedByDisplayName()
    {
        var supervisor = NewUserRecord("zoe");
        var agent = NewUserRecord("Adam");
        var defaultAccess = NewUserRecord("Max");
        provider.Setup(p => p.ListRoleAssignmentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new UserRoleAssignment(supervisor.Id, TestOptions.SupervisorRoleId),
            new UserRoleAssignment(agent.Id, TestOptions.AgentRoleId),
            new UserRoleAssignment(defaultAccess.Id, Guid.Empty),
        ]);
        provider.Setup(p => p.GetUsersAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([supervisor, agent, defaultAccess]);

        var result = await CreateSut().ListUsersAsync(Token);

        Assert.Equal(
        [
            new PartnerUser(agent.Id, "Adam", "adam@example.com", "Agent"),
            new PartnerUser(defaultAccess.Id, "Max", "max@example.com", null),
            new PartnerUser(supervisor.Id, "zoe", "zoe@example.com", "Supervisor"),
        ], result);
    }

    [Fact]
    public async Task ListUsersAsync_UserAssignedTwice_LoadsTheUserOnce()
    {
        var user = NewUserRecord();
        provider.Setup(p => p.ListRoleAssignmentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new UserRoleAssignment(user.Id, TestOptions.AgentRoleId),
            new UserRoleAssignment(user.Id, TestOptions.SupervisorRoleId),
        ]);
        provider.Setup(p => p.GetUsersAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync([user]);

        var result = await CreateSut().ListUsersAsync(Token);

        Assert.Single(result);
        provider.Verify(p => p.GetUsersAsync(It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 1), It.IsAny<CancellationToken>()));
    }

    // ---- GetUserAsync ----

    [Theory]
    [InlineData("../me")]
    [InlineData("")]
    public async Task GetUserAsync_IdIsNotAGuid_ReturnsNullWithoutCallingGraph(string id)
    {
        var result = await CreateSut().GetUserAsync(id, Token);

        Assert.Null(result);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetUserAsync_UserNotFound_ReturnsNull()
    {
        provider.Setup(p => p.GetUserAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync((UserRecord?)null);

        var result = await CreateSut().GetUserAsync(userId, Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetUserAsync_AssignedUser_ReturnsUserWithRoleName()
    {
        var user = NewUserRecord();
        provider.Setup(p => p.GetUserAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        provider.Setup(p => p.GetRoleIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(TestOptions.SupervisorRoleId);

        var result = await CreateSut().GetUserAsync(user.Id, Token);

        Assert.Equal(new PartnerUser(user.Id, user.DisplayName, user.Email, "Supervisor"), result);
    }

    [Fact]
    public async Task GetUserAsync_UserWithoutAssignment_ReturnsUserWithNullRole()
    {
        var user = NewUserRecord();
        provider.Setup(p => p.GetUserAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        provider.Setup(p => p.GetRoleIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var result = await CreateSut().GetUserAsync(user.Id, Token);

        Assert.NotNull(result);
        Assert.Null(result.Role);
        provider.Verify(p => p.GetAppRolesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- DeleteUserAsync ----

    [Fact]
    public async Task DeleteUserAsync_IdIsNotAGuid_ThrowsUserNotFoundWithoutCallingGraph()
    {
        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().DeleteUserAsync("not-an-id", Token));

        Assert.Equal(PartnerUserErrorCode.UserNotFound, exception.Code);
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteUserAsync_ExistingUser_DeletesIt()
    {
        await CreateSut().DeleteUserAsync(userId, Token);

        provider.Verify(p => p.DeleteUserAsync(userId, Token), Times.Once);
    }
}
