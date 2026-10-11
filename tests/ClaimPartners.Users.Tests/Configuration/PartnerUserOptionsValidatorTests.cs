using ClaimPartners.Users.Configuration;
using ClaimPartners.Users.Tests.Shared;

namespace ClaimPartners.Users.Tests.Configuration;

public sealed class PartnerUserOptionsValidatorTests
{
    private readonly PartnerUserOptionsValidator validator = new();

    [Fact]
    public void Validate_CompleteOptionsWithSecret_Succeeds()
    {
        var result = validator.Validate(null, TestOptions.Create());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_CompleteOptionsWithCertificate_Succeeds()
    {
        var options = TestOptions.Create();
        options.GraphClientSecret = null;
        options.GraphCertificateThumbprint = "ABCDEF0123456789";

        var result = validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("secret", "ABCDEF0123456789")]
    public void Validate_NotExactlyOneCredential_Fails(string? secret, string? thumbprint)
    {
        var options = TestOptions.Create();
        options.GraphClientSecret = secret;
        options.GraphCertificateThumbprint = thumbprint;

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("exactly one of GraphClientSecret or GraphCertificateThumbprint", result.FailureMessage);
    }

    [Theory]
    [InlineData(nameof(PartnerUserOptions.TenantId), "")]
    [InlineData(nameof(PartnerUserOptions.TenantId), "claimdev")]
    [InlineData(nameof(PartnerUserOptions.GraphClientId), "00000000-0000-0000-0000-000000000000")]
    [InlineData(nameof(PartnerUserOptions.AppServicePrincipalId), "not-a-guid")]
    public void Validate_IdIsNotAGuid_FailsNamingTheKey(string key, string value)
    {
        var options = TestOptions.Create();
        typeof(PartnerUserOptions).GetProperty(key)!.SetValue(options, value);

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"PartnerUsers:{key}", result.FailureMessage);
    }

    [Fact]
    public void Validate_MissingTenantDomain_Fails()
    {
        var options = TestOptions.Create();
        options.TenantDomain = " ";

        var result = validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("PartnerUsers:TenantDomain", result.FailureMessage);
    }
}
