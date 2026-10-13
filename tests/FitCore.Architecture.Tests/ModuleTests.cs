using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FitCore.Platform.Modules;

namespace FitCore.Architecture.Tests;

/// <summary>Module boundaries (ADR 0003 decision 2).</summary>
public sealed partial class ModuleTests
{
    private static readonly string[] AdrModules =
    [
        "Identity", "Catalog", "Nutrition", "Training", "Progress", "Targets",
        "Care", "Sync", "Billing", "Integrations", "DataRights", "Audit",
    ];

    public static TheoryData<string> Modules => [.. Solution.Modules.Select(m => m.ModuleName())];

    [Fact]
    public void The_modules_are_the_ones_ADR_0003_names()
    {
        Assert.Equal(AdrModules.Order(StringComparer.Ordinal), Solution.Modules.Select(m => m.ModuleName()));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_has_one_entry_point_that_owns_its_snake_case_schema(string name)
    {
        var assembly = ModuleAssembly(name);
        var entryPoints = assembly.GetTypes().Where(t => typeof(IModule).IsAssignableFrom(t)).ToList();

        var entryPoint = Assert.Single(entryPoints);
        Assert.Equal($"FitCore.Modules.{name}.{name}Module", entryPoint.FullName);

        var module = (IModule)Activator.CreateInstance(entryPoint)!;
        Assert.Equal(name, module.Name);
        Assert.Equal(SnakeCase(name), module.Schema);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_exposes_only_its_entry_point_and_contracts(string name)
    {
        var contracts = $"FitCore.Modules.{name}.Contracts";
        var exposed = ModuleAssembly(name).GetExportedTypes()
            .Where(t => !typeof(IModule).IsAssignableFrom(t))
            .Where(t => t.Namespace != contracts && t.Namespace?.StartsWith(contracts + ".", StringComparison.Ordinal) != true);

        Assert.Empty(exposed.Select(t => t.FullName));
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_shares_internals_only_with_its_own_tests(string name)
    {
        // A module can reach another module only through its public types, which the test above limits to the entry
        // point and the contracts. InternalsVisibleTo would open the rest, so only the module's own tests may have it.
        var friends = ModuleAssembly(name).GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0]);

        Assert.All(friends, friend => Assert.Equal($"FitCore.Modules.{name}.Tests", friend));
    }

    [Theory]
    [InlineData("FitCore.Web")]
    [InlineData("FitCore.Worker")]
    public void Host_loads_every_module(string host)
    {
        var references = Solution.Load(host).FitCoreReferences();

        Assert.All(Solution.Modules, m => Assert.Contains(m.GetName().Name, references));
    }

    private static Assembly ModuleAssembly(string name) => Solution.Load(Solution.ModulePrefix + name);

    private static string SnakeCase(string name) => UpperAfterLower().Replace(name, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex UpperAfterLower();
}
