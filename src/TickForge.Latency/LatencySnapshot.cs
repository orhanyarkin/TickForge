using System.Globalization;

namespace TickForge.Latency;

/// <summary>
/// An immutable point-in-time view of the latency distribution, in nanoseconds.
/// Produced by <see cref="LatencyRecorder.Snapshot"/>.
/// </summary>
public readonly record struct LatencySnapshot(
    long P50,
    long P90,
    long P99,
    long P999,
    long Max,
    long Count)
{
    /// <summary>An empty snapshot (no samples recorded).</summary>
    public static LatencySnapshot Empty => default;

    private static double ToMicros(long nanos) => nanos / 1_000.0;

    /// <summary>Compact one-line form using microseconds, e.g. for the console.</summary>
    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "p50={0:F1}µs p90={1:F1}µs p99={2:F1}µs p99.9={3:F1}µs max={4:F1}µs n={5}",
            ToMicros(P50), ToMicros(P90), ToMicros(P99), ToMicros(P999), ToMicros(Max), Count);
}
