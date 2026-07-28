using System.Xml.Linq;

namespace PostgreManagementStudio.Postgres.Tests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProductionReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["PostgreManagementStudio.Core"] = [],
            ["PostgreManagementStudio.Results"] = ["PostgreManagementStudio.Core"],
            ["PostgreManagementStudio.Application"] = ["PostgreManagementStudio.Core", "PostgreManagementStudio.Results"],
            ["PostgreManagementStudio.Postgres"] = ["PostgreManagementStudio.Core", "PostgreManagementStudio.Application"],
            ["PostgreManagementStudio.Desktop"] = ["PostgreManagementStudio.Application", "PostgreManagementStudio.Postgres", "PostgreManagementStudio.Results"],
        };

    [Fact]
    public void ProductionProjectReferencesFollowArchitectureBaseline()
    {
        var root = FindRepositoryRoot();
        foreach (var project in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var actual = XDocument.Load(project)
                .Descendants("ProjectReference")
                .Select(x => Path.GetFileNameWithoutExtension((string)x.Attribute("Include")!))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            var expected = AllowedProductionReferences[name].OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
            Assert.DoesNotContain(actual, x => x.EndsWith(".Tests", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void NpgsqlConnectionsAreConstructedOnlyByFactory()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "NpgsqlConnectionFactory.cs")
            .Where(path => File.ReadAllText(path).Contains("new NpgsqlConnection(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void LowerLayersDoNotReferenceDesktopOrWpf()
    {
        var root = FindRepositoryRoot();
        var lowerLayers = new[]
        {
            "PostgreManagementStudio.Core",
            "PostgreManagementStudio.Results",
            "PostgreManagementStudio.Application",
            "PostgreManagementStudio.Postgres",
        };
        var forbidden = new[] { "PostgreManagementStudio.Desktop", "System.Windows", "PresentationFramework" };
        var offenders = lowerLayers
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, "src", project), "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Where(path => forbidden.Any(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PostgreManagementStudio.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
