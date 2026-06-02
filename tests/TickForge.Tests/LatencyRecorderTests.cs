using TickForge.Latency;
using Xunit;

namespace TickForge.Tests;

public sealed class LatencyRecorderTests
{
    [Fact]
    public void EmptyRecorder_ReturnsEmptySnapshot()
    {
        var recorder = new LatencyRecorder();

        Assert.Equal(LatencySnapshot.Empty, recorder.Snapshot());
    }

    [Fact]
    public void Percentiles_AreAccurate_OnAKnownDistribution()
    {
        var recorder = new LatencyRecorder();
        for (var i = 0; i < 1_000; i++)
            recorder.Record(1_000); // 1 µs each

        var snapshot = recorder.Snapshot();

        Assert.Equal(1_000, snapshot.Count);
        // HdrHistogram with 3 significant digits keeps values within ~0.1%.
        Assert.InRange(snapshot.P50, 995, 1_010);
        Assert.InRange(snapshot.Max, 995, 1_010);
    }

    [Fact]
    public void NegativeSamples_AreIgnored()
    {
        var recorder = new LatencyRecorder();

        recorder.Record(-5);

        Assert.Equal(LatencySnapshot.Empty, recorder.Snapshot());
    }

    [Fact]
    public void CoordinatedOmissionCorrection_InflatesTheTail_OnAStall()
    {
        // Same samples fed to both: 10k fast 1µs samples and one 100ms stall.
        const long expectedInterval = 1_000;     // 1 µs expected cadence
        const long stallNanos = 100_000_000;     // 100 ms stall

        var corrected = new LatencyRecorder(expectedIntervalNanos: expectedInterval);
        var raw = new LatencyRecorder(expectedIntervalNanos: 0);

        for (var i = 0; i < 10_000; i++)
        {
            corrected.Record(1_000);
            raw.Record(1_000);
        }

        corrected.Record(stallNanos);
        raw.Record(stallNanos);

        var c = corrected.Snapshot();
        var r = raw.Snapshot();

        // Correction back-fills the samples the stall hid, so the corrected
        // histogram has many more samples and a far heavier tail.
        Assert.True(c.Count > r.Count, $"corrected count {c.Count} should exceed raw {r.Count}");
        Assert.True(c.P99 > r.P99, $"corrected p99 {c.P99} should exceed raw {r.P99}");
    }
}
