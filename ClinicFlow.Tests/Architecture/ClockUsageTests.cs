using System.Text.RegularExpressions;

namespace ClinicFlow.Tests.Architecture
{
    // "Now" comes only from IClock (REVIEW.md §4.1, docs/v2/06_TECHNICAL_DESIGN.md §6), so rules can be tested with
    // a frozen clock and every timestamp is UTC. This scans the source of every production project, the host included.
    // Comment lines are skipped; test projects may read the real clock to build their fixtures.
    public partial class ClockUsageTests
    {
        private static readonly string[] TestProjects = ["ClinicFlow.Tests", "ClinicFlow.IntegrationTests"];

        // The one place that hands TimeProvider.System to Clock
        private const string ClockRegistration = "Services/Extensions/CoreServiceExtensions.cs";

        [GeneratedRegex(@"\bDateTime(Offset)?\s*\.\s*(UtcNow|Now|Today)\b")]
        private static partial Regex SystemClockRead();

        [GeneratedRegex(@"\bTimeProvider\s*\.\s*System\b")]
        private static partial Regex SystemTimeProvider();

        [Fact]
        public void ProductionCode_ReadsTheTimeOnlyThroughIClock()
        {
            var root = SolutionRoot();

            var violations = ProductionSourceFiles(root)
                .SelectMany(file => File.ReadLines(file).Select((line, index) => (File: file, Line: line, Number: index + 1)))
                .Where(l => !l.Line.TrimStart().StartsWith("//"))
                .Where(l => SystemClockRead().IsMatch(l.Line)
                    || (SystemTimeProvider().IsMatch(l.Line) && RelativePath(root, l.File) != ClockRegistration))
                .Select(l => $"{RelativePath(root, l.File)}:{l.Number}: {l.Line.Trim()}")
                .ToList();

            Assert.True(violations.Count == 0,
                $"Read the time through IClock instead (REVIEW.md §4.1):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        [Fact]
        public void ProductionSourceFiles_IncludeEveryLayerAndTheHost()
        {
            // Guards the scan itself: a wrong root or filter would make the test above pass on nothing
            var projects = ProductionSourceFiles(SolutionRoot())
                .Select(f => Path.GetFileName(ProjectDirectory(f)))
                .ToHashSet();

            Assert.Superset(
                new HashSet<string> { "ClinicFlow", "Domain", "Infrastructure", "Persistance", "Presentation", "Services", "Services.Abstraction", "Shared" },
                projects);
        }

        private static IEnumerable<string> ProductionSourceFiles(string root)
            => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .OfType<string>()
                .Where(dir => !TestProjects.Contains(Path.GetFileName(dir)))
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                .Where(file => !IsBuildOutput(file));

        private static bool IsBuildOutput(string file)
            => file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is var parts
               && (parts.Contains("bin") || parts.Contains("obj"));

        private static string ProjectDirectory(string file)
        {
            var dir = Path.GetDirectoryName(file)!;
            while (!Directory.EnumerateFiles(dir, "*.csproj").Any())
                dir = Path.GetDirectoryName(dir)!;
            return dir;
        }

        private static string SolutionRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClinicFlow.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("ClinicFlow.slnx not found above the test output folder");
        }

        private static string RelativePath(string root, string file)
            => Path.GetRelativePath(root, file).Replace('\\', '/');
    }
}
