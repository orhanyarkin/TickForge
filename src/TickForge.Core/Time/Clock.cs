using System.Diagnostics;

namespace TickForge.Core.Time;

/// <summary>
/// Monotonic, high-resolution clock in nanoseconds, backed by
/// <see cref="Stopwatch.GetTimestamp"/>. Use this — never <see cref="DateTime"/> —
/// for latency measurement: it is monotonic (never goes backwards across NTP
/// adjustments) and has sub-microsecond resolution on modern hardware.
/// </summary>
public static class Clock
{
    private static readonly double NanosPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    /// <summary>Monotonic timestamp in nanoseconds. Only differences are meaningful.</summary>
    public static long NowNanos() => (long)(Stopwatch.GetTimestamp() * NanosPerTick);

    /// <summary>Convert a raw tick delta to nanoseconds.</summary>
    public static long TicksToNanos(long ticks) => (long)(ticks * NanosPerTick);
}
