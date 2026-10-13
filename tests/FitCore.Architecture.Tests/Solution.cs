using System.Reflection;

namespace FitCore.Architecture.Tests;

/// <summary>Loads the solution's assemblies from the test output directory.</summary>
internal static class Solution
{
    public const string ModulePrefix = "FitCore.Modules.";

    public static Assembly Load(string name) => Assembly.Load(name);

    /// <summary>Every module assembly, found by file name so a new module can't be missed.</summary>
    public static IReadOnlyList<Assembly> Modules { get; } =
        Directory.GetFiles(AppContext.BaseDirectory, ModulePrefix + "*.dll")
            .Select(path => Assembly.Load(Path.GetFileNameWithoutExtension(path)))
            .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The names of the FitCore assemblies this assembly actually uses. The compiler drops unused references.</summary>
    public static IEnumerable<string> FitCoreReferences(this Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("FitCore.", StringComparison.Ordinal));

    public static string ModuleName(this Assembly module) => module.GetName().Name![ModulePrefix.Length..];
}
