using System.Reflection;
using NetArchTest.Rules;
using TreasuryKit.BuildingBlocks.Application.Messaging;
using TreasuryKit.BuildingBlocks.Domain;
using TreasuryKit.BuildingBlocks.Infrastructure.Persistence;
using TreasuryKit.Core.Application;
using TreasuryKit.Core.Contracts.Tenants;
using TreasuryKit.Core.Domain.Tenants;
using TreasuryKit.Core.Infrastructure;
using TreasuryKit.Recon.Infrastructure;

namespace TreasuryKit.ArchitectureTests;

/// <summary>Enforces the module boundaries from IMPLEMENTATION_PLAN.md §2.5.</summary>
public sealed class ArchitectureRules
{
    private const string BuildingBlocksDomain = "TreasuryKit.BuildingBlocks.Domain";

    private static readonly Assembly[] BuildingBlocks =
    [
        typeof(Entity<>).Assembly,
        typeof(IDispatcher).Assembly,
        typeof(ModuleDbContext).Assembly,
    ];

    // Adding a module: add a line here.
    private static readonly IReadOnlyDictionary<string, ModuleAssemblies> Modules = new Dictionary<string, ModuleAssemblies>
    {
        ["Core"] = new(
            "Core",
            typeof(Tenant).Assembly,
            typeof(ICoreDbContext).Assembly,
            typeof(CoreModule).Assembly,
            typeof(TenantCreated).Assembly),
        ["Recon"] = new(
            "Recon",
            typeof(Recon.Domain.AssemblyMarker).Assembly,
            typeof(Recon.Application.AssemblyMarker).Assembly,
            typeof(ReconModule).Assembly,
            typeof(Recon.Contracts.AssemblyMarker).Assembly),
    };

    public static TheoryData<string> ModuleNames => new(Modules.Keys);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Domain_depends_only_on_BuildingBlocks_Domain(string module)
    {
        var m = Modules[module];
        var result = Types.InAssembly(m.Domain)
            .Should()
            .OnlyHaveDependenciesOn("System", BuildingBlocksDomain, m.Namespace("Domain"))
            .GetResult();

        AssertSuccessful(result);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Application_does_not_depend_on_Infrastructure(string module)
    {
        var m = Modules[module];
        var result = Types.InAssembly(m.Application)
            .ShouldNot()
            .HaveDependencyOnAny(m.Namespace("Infrastructure"), "TreasuryKit.BuildingBlocks.Infrastructure")
            .GetResult();

        AssertSuccessful(result);
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_references_other_modules_only_through_Contracts(string module)
    {
        var m = Modules[module];
        var others = Modules.Values.Where(o => o != m).ToList();
        var forbiddenAssemblies = others.SelectMany(o => o.NonContracts).Select(a => a.GetName().Name!).ToHashSet();
        var forbiddenNamespaces = others
            .SelectMany(o => new[] { o.Namespace("Domain"), o.Namespace("Application"), o.Namespace("Infrastructure") })
            .ToArray();

        foreach (var assembly in m.All)
        {
            var badReferences = assembly.GetReferencedAssemblies()
                .Select(r => r.Name!)
                .Where(forbiddenAssemblies.Contains)
                .ToList();
            Assert.True(badReferences.Count == 0, $"{assembly.GetName().Name} references {string.Join(", ", badReferences)}");

            AssertSuccessful(Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbiddenNamespaces).GetResult());
        }
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Contracts_do_not_depend_on_any_Domain(string module)
    {
        var domainNamespaces = Modules.Values.Select(o => o.Namespace("Domain")).ToArray();
        var result = Types.InAssembly(Modules[module].Contracts)
            .ShouldNot()
            .HaveDependencyOnAny(domainNamespaces)
            .GetResult();

        AssertSuccessful(result);
    }

    [Fact]
    public void BuildingBlocks_do_not_depend_on_modules()
    {
        var moduleNamespaces = Modules.Keys.Select(name => $"TreasuryKit.{name}").ToArray();
        foreach (var assembly in BuildingBlocks)
        {
            AssertSuccessful(Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(moduleNamespaces).GetResult());
        }
    }

    [Fact]
    public void BuildingBlocks_Domain_has_no_framework_dependencies()
    {
        var result = Types.InAssembly(typeof(Entity<>).Assembly)
            .Should()
            .OnlyHaveDependenciesOn("System", BuildingBlocksDomain)
            .GetResult();

        AssertSuccessful(result);
    }

    private static void AssertSuccessful(NetArchTest.Rules.TestResult result) =>
        Assert.True(
            result.IsSuccessful,
            "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []));

    private sealed record ModuleAssemblies(
        string Name,
        Assembly Domain,
        Assembly Application,
        Assembly Infrastructure,
        Assembly Contracts)
    {
        public IEnumerable<Assembly> All => [Domain, Application, Infrastructure, Contracts];

        public IEnumerable<Assembly> NonContracts => [Domain, Application, Infrastructure];

        public string Namespace(string layer) => $"TreasuryKit.{Name}.{layer}";
    }
}
