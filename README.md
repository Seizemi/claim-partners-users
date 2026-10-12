# ClaimPartners.Users

NuGet package managing partner users (Agents, Supervisors) of the Claim portal on Microsoft Entra External ID through Microsoft Graph. How to use it: [src/ClaimPartners.Users/README.md](src/ClaimPartners.Users/README.md) (the package's own README).

| Folder | Content |
|---|---|
| `src/ClaimPartners.Users` | The package |
| `tests/ClaimPartners.Users.Tests` | Unit and architecture tests |
| `tools/ClaimPartners.Users.Playground` | Console that runs the package against the dev tenant, for debugging (never packed) |

## Releasing

The package is published to GitHub Packages (`https://nuget.pkg.github.com/Seizemi/index.json`) by `.github/workflows/package-publish.yml` when a `pkg-v<version>` tag is pushed. The workflow runs the tests, packs with the tag's version and pushes the `.nupkg`.

```powershell
git tag pkg-v1.0.0
git push origin pkg-v1.0.0
```

- **Versions** follow semantic versioning: MAJOR for breaking changes to `IPartnerUserService`, the options or the exceptions (ClaimApi is updated in the same change); MINOR for new features; PATCH for fixes.
- **Trying a change from ClaimApi before releasing it:** push a prerelease tag (`pkg-v1.1.0-rc.1`, then `rc.2`...) and reference that version in ClaimApi.
- **Published versions are immutable:** a tag is never reused. NuGet clients cache every version, so a changed package always needs a new version.
- The `<Version>` in the csproj is only the default for local builds; CI overrides it.
