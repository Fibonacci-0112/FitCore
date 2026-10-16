namespace FitCore.Architecture.Tests;

/// <summary>Which projects may depend on which (architecture-options §B.2, NFR-MNT-4).</summary>
public sealed class LayeringTests
{
    // The shared rules run on devices and in the API, so they may use only pure base-library assemblies: no I/O,
    // console, processes, networking or data access. Add an assembly here only if it has no side effects.
    private static readonly string[] DomainAllowed =
    [
        "System.Runtime", "System.Collections", "System.Collections.Immutable", "System.Linq", "System.Memory",
        "System.Runtime.Numerics", "System.Text.RegularExpressions",
    ];

    [Fact]
    public void Domain_uses_only_pure_base_library_assemblies()
    {
        var references = Solution.Load("FitCore.Domain").GetReferencedAssemblies().Select(a => a.Name!);

        Assert.All(references, name => Assert.Contains(name, DomainAllowed));
    }

    [Theory]
    [InlineData("ProjectReference")]
    [InlineData("PackageReference")]
    [InlineData("FrameworkReference")]
    public void Domain_declares_no_references(string item)
    {
        Assert.Empty(Solution.Declared("FitCore.Domain", item));
    }

    [Theory]
    [InlineData("FitCore.Contracts", new[] { "FitCore.Domain" })]
    [InlineData("FitCore.Client.Core", new[] { "FitCore.Domain", "FitCore.Contracts" })]
    [InlineData("FitCore.Client.Auth", new[] { "FitCore.Domain", "FitCore.Contracts", "FitCore.Client.Core" })]
    [InlineData("FitCore.Web.Client", new[] { "FitCore.Domain", "FitCore.Contracts", "FitCore.Client.Core" })]
    [InlineData("FitCore.Platform", new string[0])]
    public void Project_uses_only_allowed_FitCore_projects(string project, string[] allowed)
    {
        // Compiled references show what the code uses; declared ones also catch a reference it doesn't use yet.
        var references = Solution.Load(project).FitCoreReferences().Concat(Solution.Declared(project, "ProjectReference"));

        Assert.All(references, name => Assert.Contains(name, allowed));
    }

    [Fact]
    public void The_device_app_uses_only_client_projects()
    {
        // FitCore.Maui builds only with the MAUI workload, so its declared references stand in for compiled ones.
        string[] allowed = ["FitCore.Domain", "FitCore.Contracts", "FitCore.Client.Core", "FitCore.Client.Auth"];

        Assert.All(Solution.Declared("FitCore.Maui", "ProjectReference"), name => Assert.Contains(name, allowed));
    }

    [Fact]
    public void Browser_code_never_reaches_Supabase_Auth()
    {
        // The web signs members in on the server, and no token reaches WebAssembly (ADR 0005). These are the projects the
        // Blazor client is built from; the test above keeps it to them.
        string[] browser = ["FitCore.Domain", "FitCore.Contracts", "FitCore.Client.Core", "FitCore.Web.Client"];

        foreach (var project in browser)
        {
            var references = Solution.Load(project).GetReferencedAssemblies().Select(a => a.Name!)
                .Concat(Solution.Declared(project, "ProjectReference"))
                .Concat(Solution.Declared(project, "PackageReference"));

            Assert.DoesNotContain(references, name =>
                name == "FitCore.Client.Auth" || name.StartsWith("Supabase", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Client_projects_never_reach_server_code()
    {
        // Clients go through the API (ADR 0003 decision 4); the Blazor client also ships to browsers.
        string[] clients = ["FitCore.Domain", "FitCore.Contracts", "FitCore.Client.Core", "FitCore.Client.Auth", "FitCore.Web.Client"];

        foreach (var client in clients)
        {
            var references = Solution.Load(client).GetReferencedAssemblies().Select(a => a.Name!)
                .Concat(Solution.Declared(client, "PackageReference"));

            Assert.DoesNotContain(references, name =>
                name.StartsWith(Solution.ModulePrefix, StringComparison.Ordinal)
                || name is "FitCore.Platform" or "FitCore.Web" or "FitCore.Worker"
                || name.StartsWith("Npgsql", StringComparison.Ordinal));
        }
    }
}
