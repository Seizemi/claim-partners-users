using ClaimPartners.Users.Domain;
using NetArchTest.Rules;

namespace ClaimPartners.Users.Tests.Architecture;

/// <summary>
/// The layers are folders of one assembly; these rules keep their dependencies pointing inwards.
/// </summary>
public sealed class ArchitectureTests
{
    private const string Root = "ClaimPartners.Users";
    private const string Domain = $"{Root}.Domain";
    private const string Core = $"{Root}.Core";
    private const string Configuration = $"{Root}.Configuration";
    private const string Providers = $"{Root}.Providers";
    private const string DependencyInjection = $"{Root}.DependencyInjection";

    private static readonly string[] GraphAndAzure = ["Microsoft.Graph", "Microsoft.Kiota", "Azure"];

    [Fact]
    public void Domain_DependsOnNothingElseInThePackage()
    {
        AssertNoDependency(Domain, [Core, Configuration, Providers, DependencyInjection, .. GraphAndAzure]);
    }

    [Fact]
    public void Configuration_DependsOnNoOtherLayer()
    {
        AssertNoDependency(Configuration, [Core, Providers, DependencyInjection, .. GraphAndAzure]);
    }

    [Fact]
    public void Core_DependsNeitherOnProvidersNorOnGraph()
    {
        AssertNoDependency(Core, [Providers, DependencyInjection, .. GraphAndAzure]);
    }

    [Fact]
    public void Providers_DoNotDependOnDependencyInjection()
    {
        AssertNoDependency(Providers, [DependencyInjection]);
    }

    [Fact]
    public void Package_DoesNotReferenceAspNetCore()
    {
        Assert.DoesNotContain(
            typeof(PartnerUser).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicTypes_AreOnlyTheContract()
    {
        string[] expected =
        [
            "ClaimPartners.Users.Configuration.IGraphClientAssertionProvider",
            "ClaimPartners.Users.Configuration.PartnerUserOptions",
            "ClaimPartners.Users.Core.CreatePartnerUserRequest",
            "ClaimPartners.Users.Core.IPartnerUserService",
            "ClaimPartners.Users.DependencyInjection.PartnerUserServiceCollectionExtensions",
            "ClaimPartners.Users.Domain.PartnerUser",
            "ClaimPartners.Users.Domain.PartnerUserErrorCode",
            "ClaimPartners.Users.Domain.PartnerUserException",
        ];

        var actual = typeof(PartnerUser).Assembly.GetExportedTypes().Select(type => type.FullName).Order();

        Assert.Equal(expected, actual);
    }

    private static void AssertNoDependency(string layer, string[] forbidden)
    {
        var result = Types.InAssembly(typeof(PartnerUser).Assembly)
            .That().ResideInNamespace(layer)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{layer} must not depend on: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
