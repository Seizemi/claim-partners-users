# ClaimPartners.Users

Manages the users of a **Microsoft Entra External ID** tenant and their app role on a web application, through Microsoft Graph with the app's own credentials (client credentials). It knows nothing about HTTP, sessions or the current user: authorization rules (who may create or delete whom) belong to the host application.

## Registration

```csharp
using ClaimPartners.Users.DependencyInjection;

builder.Services.AddPartnerUserManagement(builder.Configuration);
```

Then inject `ClaimPartners.Users.Core.IPartnerUserService`.

## Configuration (`PartnerUsers` section)

```json
"PartnerUsers": {
  "TenantId": "<external-tenant-id>",
  "TenantDomain": "<subdomain>.onmicrosoft.com",
  "GraphClientId": "<graph-app-client-id>",
  "GraphClientSecret": "<dev only, from user-secrets>",
  "GraphCertificateThumbprint": "<or: certificate in the CurrentUser/LocalMachine My store>",
  "AppServicePrincipalId": "<web-app-service-principal-object-id>"
}
```

Exactly one of `GraphClientSecret` or `GraphCertificateThumbprint` must be set. Invalid settings stop the host at startup. Unknown keys (for example the host's `AssignableRoles`) are ignored.

### Client assertions supplied by the host (since 1.1.0)

When the certificate isn't in a local store (for example in Azure Key Vault, read with a managed identity), the host signs the client assertions itself:

```csharp
builder.Services.AddPartnerUserManagement(builder.Configuration, serviceProvider => new MyAssertionProvider(...));
```

`Configuration.IGraphClientAssertionProvider.GetAssertionAsync(clientId, audience, cancellationToken)` returns a JWT (RFC 7523) signed with a certificate registered on the Graph app registration: issuer and subject `clientId`, audience `audience` (the external tenant's token endpoint). It is called each time a Graph token is requested (about once an hour). The package still builds the credential for its own `TenantId` and `GraphClientId`. `GraphClientSecret` and `GraphCertificateThumbprint` must then be empty.

App role IDs are not configured. The first operation that needs them reads the web app's enabled app roles from Graph (`GET /servicePrincipals/{AppServicePrincipalId}?$select=appRoles`) and keeps them, matched by **value** (`Supervisor`, `Agent`, ...), for the lifetime of the application. A role added or renamed in Entra is seen after a restart. If that read fails, the operation fails with `GraphError` and the next one tries again.

The Graph app registration needs these **application** permissions, with admin consent: `User.ReadWrite.All`, `AppRoleAssignment.ReadWrite.All`, `Application.Read.All`.

## Operations

| Method | Behavior |
|---|---|
| `CreateUserAsync` | Fails with `UnknownRole` if the web app has no enabled app role with that value and with `UserAlreadyExists` if the email is taken. Creates the user with an email identity and a random password that is discarded (the user sets their own through *Forgot password*). The user is created **without a role** (`Role` is `null`): call `AssignRoleAsync` next. |
| `AssignRoleAsync` | Assigns the role; an existing assignment of the same role counts as success, so it can be retried safely. Entra needs from a few seconds to over a minute before a new user can be assigned: the call retries for a few seconds while Graph answers "does not exist" or "Not a valid reference update", then fails with `GraphError`. Retry later (for example from a background job) rather than in the user's request. |
| `ListUsersAsync` | Users assigned to the web app, with their role value (`null` for a role the app no longer defines). |
| `GetUserAsync` | The user, or `null`. `Role` is `null` without an assignment on the app. |
| `DeleteUserAsync` | Soft delete (recoverable for 30 days). `UserNotFound` if the user doesn't exist. |

Every failure is a `PartnerUserException` with a `PartnerUserErrorCode`: `UserAlreadyExists`, `UserNotFound`, `UnknownRole`, `GraphError`. Logs never contain a password, a credential or an email in full.
