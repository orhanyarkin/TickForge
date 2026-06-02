using HdrHistogram;

namespace TickForge.Latency;

/// <summary>
/// Records internal tick-to-process latencies (nanoseconds) into an
/// HdrHistogram and produces percentile snapshots.
///
/// Recording goes through <see cref="HistogramBase.RecordValueWithExpectedInterval"/>
/// to correct for <b>coordinated omission</b>: when a consumer stalls it fails to
/// take the very samples the stall caused, which silently hides the long tail.
/// Given an expected interval between samples, HdrHistogram back-fills the
/// synthetic samples a stall would have produced, restoring an honest tail.
///
/// The underlying histogram is concurrent, so a feed thread can record while a
/// reporting thread takes a <see cref="Snapshot"/> from an isolated copy.
/// </summary>
public sealed class LatencyRecorder
{
    // 1 ns .. ~60 s, 3 significant digits. Covers everything from a few-hundred-ns
    // book apply up to a multi-second stall without losing tail resolution.
    private const long LowestDiscernibleNanos = 1;
    private const long HighestTrackableNanos = 60_000_000_000;
    private const int SignificantDigits = 3;

    private readonly LongConcurrentHistogram _histogram;
    private readonly long _expectedIntervalNanos;

    /// <param name="expectedIntervalNanos">
    /// The interval at which samples are expected to arrive in steady state. Used
    /// for coordinated-omission correction. Pass <c>0</c> to disable correction.
    /// </param>
    public LatencyRecorder(long expectedIntervalNanos = 0)
    {
        _expectedIntervalNanos = expectedIntervalNanos;
        _histogram = new LongConcurrentHistogram(
            LowestDiscernibleNanos, HighestTrackableNanos, SignificantDigits);
    }

    /// <summary>Record one latency sample in nanoseconds. Negative values are ignored.</summary>
    public void Record(long valueNanos)
    {
        if (valueNanos < 0)
            return;

        // HdrHistogram clamps to the trackable range; guard the top end explicitly
        // so a pathological sample never throws on the hot path.
        if (valueNanos > HighestTrackableNanos)
            valueNanos = HighestTrackableNanos;

        if (_expectedIntervalNanos > 0)
            _histogram.RecordValueWithExpectedInterval(valueNanos, _expectedIntervalNanos);
        else
            _histogram.RecordValue(valueNanos);
    }

    /// <summary>
    /// Take an immutable percentile snapshot. Reads from an isolated copy so it
    /// never races concurrent <see cref="Record"/> calls.
    /// </summary>
    public LatencySnapshot Snapshot()
    {
        var copy = _histogram.Copy();
        if (copy.TotalCount == 0)
            return LatencySnapshot.Empty;

        return new LatencySnapshot(
            P50: copy.GetValueAtPercentile(50),
            P90: copy.GetValueAtPercentile(90),
            P99: copy.GetValueAtPercentile(99),
            P999: copy.GetValueAtPercentile(99.9),
            Max: copy.GetMaxValue(),
            Count: copy.TotalCount);
    }
}
