namespace FitCore.Architecture.Tests;

/// <summary>Which projects may depend on which (architecture-options §B.2, NFR-MNT-4).</summary>
public sealed class LayeringTests
{
    [Fact]
    public void Domain_depends_only_on_the_base_class_library()
    {
        // The shared rules run on devices and in the API, so they can't do I/O or depend on any framework.
        var references = Solution.Load("FitCore.Domain").GetReferencedAssemblies().Select(a => a.Name!);

        Assert.All(references, name => Assert.True(
            name is "System.Runtime" or "netstandard" || name.StartsWith("System.", StringComparison.Ordinal) && !IsIoOrFramework(name),
            $"FitCore.Domain references {name}."));
    }

    [Theory]
    [InlineData("FitCore.Contracts", new[] { "FitCore.Domain" })]
    [InlineData("FitCore.Client.Core", new[] { "FitCore.Domain", "FitCore.Contracts" })]
    [InlineData("FitCore.Web.Client", new[] { "FitCore.Domain", "FitCore.Contracts", "FitCore.Client.Core" })]
    [InlineData("FitCore.Platform", new string[0])]
    public void Project_uses_only_allowed_FitCore_projects(string project, string[] allowed)
    {
        var references = Solution.Load(project).FitCoreReferences();

        Assert.All(references, name => Assert.Contains(name, allowed));
    }

    [Fact]
    public void Client_projects_never_reach_server_code()
    {
        // Clients go through the API (ADR 0003 decision 4); the Blazor client also ships to browsers.
        string[] clients = ["FitCore.Domain", "FitCore.Contracts", "FitCore.Client.Core", "FitCore.Web.Client"];

        foreach (var client in clients)
        {
            var references = Solution.Load(client).GetReferencedAssemblies().Select(a => a.Name!);

            Assert.DoesNotContain(references, name =>
                name.StartsWith(Solution.ModulePrefix, StringComparison.Ordinal)
                || name is "FitCore.Platform" or "FitCore.Web" or "FitCore.Worker"
                || name.StartsWith("Npgsql", StringComparison.Ordinal));
        }
    }

    private static bool IsIoOrFramework(string name) =>
        name.StartsWith("System.IO", StringComparison.Ordinal)
        || name.StartsWith("System.Net", StringComparison.Ordinal)
        || name.StartsWith("System.Data", StringComparison.Ordinal);
}
