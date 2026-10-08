using Claim.PartnerUsers.Core;
using Claim.PartnerUsers.Domain;
using Claim.PartnerUsers.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;

namespace Claim.PartnerUsers.Tests.Core;

public sealed class PartnerUserServiceTests
{
    private readonly Mock<IPartnerUserProvider> provider = new();
    private readonly FakeLogger<PartnerUserService> logger = new();
    private readonly string userId = Guid.NewGuid().ToString();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private PartnerUserService CreateSut() => new(provider.Object, Options.Create(TestOptions.Create()), logger);

    private static CreatePartnerUserRequest AgentRequest => new("jane@example.com", "Jane", "Agent");

    private static UserRecord NewUserRecord(string displayName = "Jane") =>
        new(Guid.NewGuid().ToString(), displayName, $"{displayName.ToLowerInvariant()}@example.com");

    private void SetupCreatedUser() =>
        provider.Setup(p => p.CreateUserAsync(It.IsAny<NewUser>(), It.IsAny<CancellationToken>())).ReturnsAsync(userId);

    // ---- CreateUserAsync ----

    [Fact]
    public async Task CreateUserAsync_UnknownRole_ThrowsUnknownRoleWithoutCallingGraph()
    {
        var request = new CreatePartnerUserRequest("jane@example.com", "Jane", "Administrator");

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().CreateUserAsync(request, Token));

        Assert.Equal(PartnerUserErrorCode.UnknownRole, exception.Code);
        provider.VerifyNoOtherCalls();
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
    public async Task CreateUserAsync_ValidRequest_CreatesUserAssignsRoleAndReturnsUser()
    {
        NewUser? created = null;
        provider.Setup(p => p.CreateUserAsync(It.IsAny<NewUser>(), It.IsAny<CancellationToken>()))
            .Callback<NewUser, CancellationToken>((user, _) => created = user)
            .ReturnsAsync(userId);

        var result = await CreateSut().CreateUserAsync(new CreatePartnerUserRequest("  jane@example.com ", " Jane Doe ", "Agent"), Token);

        Assert.Equal(new PartnerUser(userId, "Jane Doe", "jane@example.com", "Agent"), result);
        Assert.NotNull(created);
        Assert.Equal("jane@example.com", created.Email);
        Assert.Equal("Jane Doe", created.DisplayName);
        Assert.Equal(PasswordGenerator.Length, created.Password.Length);
        provider.Verify(p => p.AssignRoleAsync(userId, TestOptions.AgentRoleId, It.IsAny<CancellationToken>()), Times.Once);
        provider.Verify(p => p.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public async Task CreateUserAsync_RoleAssignmentFails_DeletesCreatedUserAndThrowsGraphError()
    {
        var assignmentError = new PartnerUserException(PartnerUserErrorCode.GraphError, "Graph failed");
        SetupCreatedUser();
        provider.Setup(p => p.AssignRoleAsync(userId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(assignmentError);

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().CreateUserAsync(AgentRequest, Token));

        Assert.Equal(PartnerUserErrorCode.GraphError, exception.Code);
        Assert.Same(assignmentError, exception.InnerException);
        provider.Verify(p => p.DeleteUserAsync(userId, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_RoleAssignmentAndCleanupFail_ThrowsGraphErrorAndLogsOrphan()
    {
        SetupCreatedUser();
        provider.Setup(p => p.AssignRoleAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PartnerUserException(PartnerUserErrorCode.GraphError, "assign failed"));
        provider.Setup(p => p.DeleteUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PartnerUserException(PartnerUserErrorCode.GraphError, "delete failed"));

        var exception = await Assert.ThrowsAsync<PartnerUserException>(() => CreateSut().CreateUserAsync(AgentRequest, Token));

        Assert.Equal(PartnerUserErrorCode.GraphError, exception.Code);
        Assert.Contains(logger.Collector.GetSnapshot(), entry => entry.Level == LogLevel.Error && entry.Message.Contains(userId));
    }

    [Fact]
    public async Task CreateUserAsync_CancelledDuringRoleAssignment_DeletesCreatedUserAndRethrows()
    {
        SetupCreatedUser();
        provider.Setup(p => p.AssignRoleAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateSut().CreateUserAsync(AgentRequest, Token));

        provider.Verify(p => p.DeleteUserAsync(userId, CancellationToken.None), Times.Once);
    }

    // ---- ListUsersAsync ----

    [Fact]
    public async Task ListUsersAsync_NoAssignment_ReturnsEmptyWithoutLoadingUsers()
    {
        provider.Setup(p => p.ListRoleAssignmentsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await CreateSut().ListUsersAsync(Token);

        Assert.Empty(result);
        provider.Verify(p => p.GetUsersAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
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
