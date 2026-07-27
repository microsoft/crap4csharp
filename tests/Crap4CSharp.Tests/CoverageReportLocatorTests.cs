namespace Microsoft.Crap4CSharp.Tests;

// All C#-specific additions -- crap4java had no locator because JaCoCo's report path was a fixed constant.
// Pure filesystem fixtures (no executor): coverlet writes coverage.cobertura.xml under a per-run GUID
// subdirectory of <root>/coverage/, and Locate must find it, return null when it is absent, and pick the
// ordinal-first path deterministically when several are present (departure #3). Each test uses a fresh temp
// tree removed in a finally.
public class CoverageReportLocatorTests
{
    [Fact]
    public void LocatesReportInGuidSubdirectory()
    {
        WithTempRoot(root =>
        {
            string report = Path.Combine(root, "coverage", "guid", "coverage.cobertura.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, "<coverage/>");

            CoverageReportLocator.Locate(root).Should().Be(report);
        });
    }

    [Fact]
    public void ReturnsNullWhenNoCoverageDirectory()
    {
        WithTempRoot(root =>
        {
            CoverageReportLocator.Locate(root).Should().BeNull();
        });
    }

    [Fact]
    public void ReturnsNullWhenCoverageDirectoryHasNoReport()
    {
        WithTempRoot(root =>
        {
            string subDir = Path.Combine(root, "coverage", "guid");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "other.xml"), "x");
            File.WriteAllText(Path.Combine(subDir, "report.trx"), "x");

            CoverageReportLocator.Locate(root).Should().BeNull();
        });
    }

    [Fact]
    public void SelectsOrdinalFirstReportWhenMultiplePresent()
    {
        WithTempRoot(root =>
        {
            // Fixed non-GUID subdir names so the ordinal winner is deterministic: "aaaa" < "bbbb".
            string first = Path.Combine(root, "coverage", "aaaa", "coverage.cobertura.xml");
            string second = Path.Combine(root, "coverage", "bbbb", "coverage.cobertura.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(first)!);
            Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            File.WriteAllText(first, "<coverage/>");
            File.WriteAllText(second, "<coverage/>");

            CoverageReportLocator.Locate(root).Should().Be(first);
        });
    }

    [Fact]
    public void ThrowsOnNullProjectRoot()
    {
        Action act = () => CoverageReportLocator.Locate(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("projectRoot");
    }

    private static void WithTempRoot(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            test(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
