namespace Microsoft.Crap4CSharp.Tests;

// All C#-specific additions -- crap4java had no locator because JaCoCo's report path was a fixed constant.
// Pure filesystem fixtures (no executor): coverlet writes coverage.cobertura.xml under a per-run GUID
// subdirectory of <root>/coverage/, and LocateAll must find it, return an EMPTY list when absent, and
// surface EVERY match in ordinal order when several are present (a multi-targeted test project; departure #3)
// -- CliApplication owns the >1 fail-fast (T22), so the locator hides no multiplicity. Each test uses a
// fresh temp tree removed in a finally.
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

            CoverageReportLocator.LocateAll(root).Should().Equal(report);
        });
    }

    [Fact]
    public void ReturnsEmptyWhenNoCoverageDirectory()
    {
        WithTempRoot(root =>
        {
            CoverageReportLocator.LocateAll(root).Should().BeEmpty();
        });
    }

    [Fact]
    public void ReturnsEmptyWhenCoverageDirectoryHasNoReport()
    {
        WithTempRoot(root =>
        {
            string subDir = Path.Combine(root, "coverage", "guid");
            Directory.CreateDirectory(subDir);
            File.WriteAllText(Path.Combine(subDir, "other.xml"), "x");
            File.WriteAllText(Path.Combine(subDir, "report.trx"), "x");

            CoverageReportLocator.LocateAll(root).Should().BeEmpty();
        });
    }

    [Fact]
    public void ReturnsAllReportsInOrdinalOrderWhenMultiplePresent()
    {
        WithTempRoot(root =>
        {
            // Fixed non-GUID subdir names so ordinal order is deterministic: "aaaa" < "bbbb". LocateAll must
            // surface BOTH (multiplicity is a T22 fail-fast signal for CliApplication, not hidden here).
            string first = Path.Combine(root, "coverage", "aaaa", "coverage.cobertura.xml");
            string second = Path.Combine(root, "coverage", "bbbb", "coverage.cobertura.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(first)!);
            Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            File.WriteAllText(first, "<coverage/>");
            File.WriteAllText(second, "<coverage/>");

            CoverageReportLocator.LocateAll(root).Should().Equal(first, second);
        });
    }

    [Fact]
    public void ThrowsOnNullProjectRoot()
    {
        Action act = () => CoverageReportLocator.LocateAll(null!);

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
