// Runs IPartnerUserService.CreateUserAsync then AssignRoleAsync against the dev tenant, as the API and its
// background worker call them. Set breakpoints in the package (PartnerUserService, GraphPartnerUserProvider) and press F5.
// It creates a REAL user in the tenant: use the dev tenant only.

using System.Diagnostics;
using ClaimPartners.Users.Core;
using ClaimPartners.Users.DependencyInjection;
using ClaimPartners.Users.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

await using var services = new ServiceCollection()
    .AddLogging(logging => logging
        .AddSimpleConsole(console => console.TimestampFormat = "HH:mm:ss.fff ")
        .SetMinimumLevel(LogLevel.Information)
        .AddFilter("ClaimPartners", LogLevel.Debug))
    .AddPartnerUserManagement(configuration)
    .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

// Same startup validation as the API: fails here with a clear message if a PartnerUsers value is missing.
IPartnerUserService partnerUsers;
try
{
    partnerUsers = services.GetRequiredService<IPartnerUserService>();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Configuration error: {exception.Message}");
    Console.Error.WriteLine("Set the PartnerUsers:* values with 'dotnet user-secrets' (README of ClaimApi, step 11).");
    return 1;
}

Console.WriteLine($"Tenant domain: {configuration["PartnerUsers:TenantDomain"]}");
var email = Ask("Email of the user to create");
var displayName = Ask("Display name");
var role = Ask("Role", defaultValue: "Agent");

if (!Confirm($"Create '{displayName}' <{email}> with role {role} in this tenant?"))
{
    return 0;
}

var stopwatch = Stopwatch.StartNew();
PartnerUser user;
try
{
    user = await partnerUsers.CreateUserAsync(new CreatePartnerUserRequest(email, displayName, role));
    Console.WriteLine($"\nCreated (no role yet) in {stopwatch.Elapsed:mm\\:ss\\.fff}: {user}");
}
catch (PartnerUserException exception)
{
    Report("Create", exception);
    return 2;
}

// The API does this in the background, retrying for up to about 50 minutes; here, you decide when to retry.
var assigned = false;
do
{
    stopwatch.Restart();
    try
    {
        await partnerUsers.AssignRoleAsync(user.Id, role);
        Console.WriteLine($"Role {role} assigned in {stopwatch.Elapsed:mm\\:ss\\.fff}.");
        assigned = true;
    }
    catch (PartnerUserException exception)
    {
        Report("Role assignment", exception);
    }
}
while (!assigned && Confirm("Retry the role assignment?"));

if (Confirm("Delete this user now?"))
{
    try
    {
        await partnerUsers.DeleteUserAsync(user.Id);
        Console.WriteLine("Deleted (soft delete: recoverable for 30 days in the admin center).");
    }
    catch (PartnerUserException exception)
    {
        Report("Delete", exception);
        return 2;
    }
}

return assigned ? 0 : 2;

void Report(string operation, PartnerUserException exception)
{
    Console.WriteLine($"\n{operation} failed after {stopwatch.Elapsed:mm\\:ss\\.fff} with {exception.Code}: {exception.Message}");
    for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
    {
        Console.WriteLine($"  caused by {inner.GetType().Name}: {inner.Message}");
    }
}

static string Ask(string label, string? defaultValue = null)
{
    while (true)
    {
        Console.Write(defaultValue is null ? $"{label}: " : $"{label} [{defaultValue}]: ");
        // null: end of input (Ctrl+Z / closed stdin), nothing will ever be typed.
        var value = Console.ReadLine()?.Trim() ?? throw new OperationCanceledException("No input.");
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (defaultValue is not null)
        {
            return defaultValue;
        }
    }
}

static bool Confirm(string question)
{
    Console.Write($"{question} (y/N): ");
    return string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
}
