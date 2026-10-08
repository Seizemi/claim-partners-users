# Claim.PartnerUsers

Manages the users of a **Microsoft Entra External ID** tenant and their app role on a web application, through Microsoft Graph with the app's own credentials (client credentials). It knows nothing about HTTP, sessions or the current user: authorization rules (who may create or delete whom) belong to the host application.

## Registration

```csharp
using Claim.PartnerUsers.DependencyInjection;

builder.Services.AddPartnerUserManagement(builder.Configuration);
```

Then inject `Claim.PartnerUsers.Core.IPartnerUserService`.

## Configuration (`PartnerUsers` section)

```json
"PartnerUsers": {
  "TenantId": "<external-tenant-id>",
  "TenantDomain": "<subdomain>.onmicrosoft.com",
  "GraphClientId": "<graph-app-client-id>",
  "GraphClientSecret": "<dev only, from user-secrets>",
  "GraphCertificateThumbprint": "<or: certificate in the CurrentUser/LocalMachine My store>",
  "AppServicePrincipalId": "<web-app-service-principal-object-id>",
  "Roles": { "Supervisor": "<app-role-guid>", "Agent": "<app-role-guid>" }
}
```

Exactly one of `GraphClientSecret` or `GraphCertificateThumbprint` must be set. Invalid settings stop the host at startup. Unknown keys (for example the host's `AssignableRoles`) are ignored.

The Graph app registration needs these **application** permissions, with admin consent: `User.ReadWrite.All`, `AppRoleAssignment.ReadWrite.All`, `Application.Read.All`.

## Operations

| Method | Behavior |
|---|---|
| `CreateUserAsync` | Fails with `UnknownRole` if the role isn't configured and with `UserAlreadyExists` if the email is taken. Creates the user with an email identity and a random password that is discarded (the user sets their own through *Forgot password*), then assigns the role. If the assignment fails, the user is deleted. |
| `ListUsersAsync` | Users assigned to the web app, with their role name. |
| `GetUserAsync` | The user, or `null`. `Role` is `null` without an assignment on the app. |
| `DeleteUserAsync` | Soft delete (recoverable for 30 days). `UserNotFound` if the user doesn't exist. |

Every failure is a `PartnerUserException` with a `PartnerUserErrorCode`: `UserAlreadyExists`, `UserNotFound`, `UnknownRole`, `GraphError`. Logs never contain a password, a credential or an email in full.
