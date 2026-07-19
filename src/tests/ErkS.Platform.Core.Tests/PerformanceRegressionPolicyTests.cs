using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class PerformanceRegressionPolicyTests
{
    [Fact]
    public void WithinThreshold_ReturnsNoIssues()
    {
        PerformanceMeasurement measurement = new("intake-500", 500, TimeSpan.FromSeconds(3), 200, 40);
        PerformanceThreshold threshold = new("intake-500", TimeSpan.FromSeconds(5), 300, 80);

        Assert.Empty(PerformanceRegressionPolicy.Evaluate(measurement, threshold));
    }

    [Fact]
    public void Regressions_ReportDurationMemoryAndHandleFailures()
    {
        PerformanceMeasurement measurement = new("album-500", 500, TimeSpan.FromSeconds(31), 900, 220);
        PerformanceThreshold threshold = new("album-500", TimeSpan.FromSeconds(30), 800, 200);

        IReadOnlyList<string> issues = PerformanceRegressionPolicy.Evaluate(measurement, threshold);

        Assert.Contains(issues, issue => issue.Contains("duration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Contains("memory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Contains("handle", StringComparison.OrdinalIgnoreCase));
    }
}
