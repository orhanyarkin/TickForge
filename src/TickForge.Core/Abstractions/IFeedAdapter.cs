namespace TickForge.Core.Abstractions;

/// <summary>
/// A market-data feed for one exchange. Implementations own the connection,
/// the exchange-specific wire protocol, and book-consistency logic (snapshot
/// bootstrap, sequence-gap detection, resync), and surface a normalized stream
/// of <see cref="LevelChange"/> batches through an <see cref="IBookSink"/>.
/// </summary>
public interface IFeedAdapter : IAsyncDisposable
{
    /// <summary>Human-readable exchange name, e.g. "Binance".</summary>
    string ExchangeName { get; }

    /// <summary>
    /// Connect, subscribe to <paramref name="instruments"/>, and pump updates
    /// into <paramref name="sink"/> until cancelled. Implementations reconnect
    /// and resync internally; the method only returns on cancellation or a
    /// non-recoverable error.
    /// </summary>
    Task RunAsync(IReadOnlyList<InstrumentId> instruments, IBookSink sink, CancellationToken ct);
}
