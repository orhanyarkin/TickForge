using System;
using System.Collections.Generic;
using TickForge.Core.Abstractions;
using TickForge.Core.Book;

namespace TickForge.Tests;

/// <summary>
/// An <see cref="IBookSink"/> test double that records every call and also keeps
/// a real <see cref="OrderBook"/> up to date so tests can assert on book state.
/// Spans are copied to arrays since a <c>ReadOnlySpan</c> cannot be stored.
/// </summary>
internal sealed class FakeBookSink : IBookSink
{
    public List<LevelChange[]> Snapshots { get; } = [];
    public List<LevelChange[]> Deltas { get; } = [];
    public List<string> Resyncs { get; } = [];
    public OrderBook Book { get; } = new();

    public void OnSnapshot(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos)
    {
        Snapshots.Add(levels.ToArray());
        Book.ApplySnapshot(levels);
    }

    public void OnDelta(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos)
    {
        Deltas.Add(levels.ToArray());
        Book.ApplyDelta(levels);
    }

    public void OnResync(InstrumentId instrument, string reason) => Resyncs.Add(reason);
}
