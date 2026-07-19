namespace ErkS.Platform.Core;

public sealed record PerformanceMeasurement(
    string Name,
    int ItemCount,
    TimeSpan Duration,
    long PeakWorkingSetMegabytes,
    int PeakHandleCount);

public sealed record PerformanceThreshold(
    string Name,
    TimeSpan MaximumDuration,
    long MaximumWorkingSetMegabytes,
    int MaximumHandleCount);

public static class PerformanceRegressionPolicy
{
    public static IReadOnlyList<string> Evaluate(
        PerformanceMeasurement measurement,
        PerformanceThreshold threshold)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(threshold);
        if (!measurement.Name.Equals(threshold.Name, StringComparison.Ordinal))
            throw new ArgumentException("Measurement and threshold names must match.", nameof(threshold));

        List<string> issues = [];
        if (measurement.Duration > threshold.MaximumDuration)
        {
            issues.Add(
                $"{measurement.Name} duration {measurement.Duration.TotalMilliseconds:0} ms exceeds " +
                $"{threshold.MaximumDuration.TotalMilliseconds:0} ms.");
        }
        if (measurement.PeakWorkingSetMegabytes > threshold.MaximumWorkingSetMegabytes)
        {
            issues.Add(
                $"{measurement.Name} peak memory {measurement.PeakWorkingSetMegabytes} MB exceeds " +
                $"{threshold.MaximumWorkingSetMegabytes} MB.");
        }
        if (measurement.PeakHandleCount > threshold.MaximumHandleCount)
        {
            issues.Add(
                $"{measurement.Name} peak handle count {measurement.PeakHandleCount} exceeds " +
                $"{threshold.MaximumHandleCount}.");
        }
        return issues;
    }
}
