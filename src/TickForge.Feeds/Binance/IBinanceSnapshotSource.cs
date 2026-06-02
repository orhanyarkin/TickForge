using System.Threading;
using System.Threading.Tasks;
using TickForge.Core.Abstractions;

namespace TickForge.Feeds.Binance;

/// <summary>
/// Fetches the Binance REST depth snapshot. Abstracted so the adapter can be
/// driven by a fake source in tests without a live HTTP call.
/// </summary>
public interface IBinanceSnapshotSource
{
    Task<BinanceSnapshot> FetchAsync(InstrumentId instrument, CancellationToken ct);
}
