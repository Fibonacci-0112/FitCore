using System.Reflection;
using System.Xml.Linq;

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

    /// <summary>The repository root: the folder that holds FitCore.slnx.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The items a project file declares, such as ProjectReference or PackageReference, by include value.</summary>
    /// <remarks>Unlike compiled references, these include references the code doesn't use yet.</remarks>
    public static IEnumerable<string> Declared(string project, string item) =>
        XDocument.Load(Path.Combine(Root, "src", project, project + ".csproj"))
            .Descendants(item)
            .Select(e => (string?)e.Attribute("Include") ?? "")
            .Select(include => item == "ProjectReference" ? Path.GetFileNameWithoutExtension(include.Replace('\\', '/')) : include);

    public static string ModuleName(this Assembly module) => module.GetName().Name![ModulePrefix.Length..];

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FitCore.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("FitCore.slnx not found above the test output directory.");
    }
}
