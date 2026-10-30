using System.Diagnostics;
using System.Globalization;

namespace ErkS.Studio;

/// <summary>
/// The steps of one long operation with how long each took, written beside its result
/// («багц шалгалт 3.1 с, альбум угсралт 21.4 с»), so a slow refresh says where the time went
/// (Мастер, 2026-10-30: «алхам бүрийн хугацаа, явц алга»).
/// </summary>
internal sealed class StudioOperationTimings
{
    private readonly List<(string Step, TimeSpan Elapsed)> steps = [];

    public T Measure<T>(string step, Func<T> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            steps.Add((step, watch.Elapsed));
        }
    }

    public void Measure(string step, Action action) =>
        Measure(step, () =>
        {
            action();
            return true;
        });

    public IReadOnlyList<(string Step, TimeSpan Elapsed)> Steps => steps;

    public override string ToString() =>
        string.Join(
            ", ",
            steps.Select(step =>
                $"{step.Step} {step.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} с"));
}
