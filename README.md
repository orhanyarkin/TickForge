<div align="center">

#  TickForge

**Çoklu borsa, düşük gecikmeli L2 order book motoru — yerleşik latency ölçümüyle.**

.NET 10 / C# 14 üzerine kurulu.

[![CI](https://github.com/orhanyarkin/TickForge/actions/workflows/ci.yml/badge.svg)](https://github.com/orhanyarkin/TickForge/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

</div>

TickForge, birden çok kripto borsasından canlı market-data alır, her borsa için
güvenilmez bir bağlantı üzerinde tutarlı bir Level-2 order book tutar (snapshot
bootstrap, sequence-gap tespiti, resync) ve kendi işleme gecikmesini dürüstçe
ölçer. İçinde bir konsol host, canlı bir web dashboard, bir benchmark paketi, bir
test projesi ve CI gelir.

> Amaç trade etmek değil. Asıl zor olan kısımları inşa etmek: protokol parsing,
> paket kaybı altında book tutarlılığı, allocation-free bir hot path ve
> savunabileceğin bir latency rakamı.

---

## Öne çıkanlar

- **Borsadan bağımsız çekirdek.** Yeni bir borsa eklemek `OrderBook`'a asla
  dokunmaz. Her adapter, wire formatını tek bir `LevelChange (Side, Price,
  Quantity)` şekline normalize eder; `Quantity == 0` ise o seviye silinir.
- **İki gerçek tutarlılık modeli.** Binance (REST snapshot + diff stream, `U`/`u`
  reconciliation) ve Coinbase (Advanced Trade `level2`, global `sequence_num`)
  kendi bootstrap ve gap → resync mantıklarını, saf ve test edilebilir bir
  reconciler arkasında izole şekilde yürütür.
- **Allocation-free hot path.** Frame'ler pooled buffer'lara okunur ve book'a
  `ReadOnlySpan<LevelChange>` olarak verilir — mesaj başına heap trafiği yok.
  Elle yazılmış bir `Utf8JsonReader` parser frame başına **0 byte** allocate
  ederken, eşdeğer source-generated yol ~5.4 KB harcar.
- **Dürüst latency.** İç tick-to-process gecikmesi, coordinated-omission
  düzeltmeli bir HdrHistogram'a kaydedilir ve p50/p90/p99/p99.9 olarak sunulur.
  Borsa-yerel gecikmesi **bilerek raporlanmaz** — saatler senkron olmadığı için
  o rakam sinyal değil, clock skew olurdu.
- **Üretim odaklı.** Central package management, `TreatWarningsAsErrors`, 29 unit
  test, bir BenchmarkDotNet projesi, çok aşamalı Dockerfile ve GitHub Actions CI.

---

## Nasıl çalışır

### Modül haritası

```
TickForge.Core        Domain + abstractions. I/O yok, bağımlılık yok.
  Abstractions/         Side, InstrumentId, LevelChange, IBookSink, IFeedAdapter
  Book/                 OrderBook, BookSide
  Time/                 Clock (monotonic nanosaniye)

TickForge.Latency     HdrHistogram üzerine LatencyRecorder + LatencyBookSink. → Core

TickForge.Feeds       Borsa başına bir adapter. → Core
  Common/               WebSocketFeedBase (connect, reconnect, frame read)
  Binance/              REST snapshot + diff-stream reconciliation
  Coinbase/             level2 + sequence_num reconciliation

TickForge.App         Konsol host: adapters → sink → book → latency.
TickForge.Web         ASP.NET host + WebSocket + dashboard.
TickForge.Benchmarks  BenchmarkDotNet micro-benchmark'lar.
TickForge.Tests       xUnit: book, gap/resync, latency, parser pariteleri.
```

Bağımlılıklar tek yöne, içeri akar: her şey `Core`'a bakar; `Core` hiçbir şeye
bakmaz.

### Veri akışı

```
borsa ──▶ feed adapter ──▶ IBookSink ──▶ OrderBook
              │                              │
        (parse + recv ts)            LatencyRecorder ──▶ konsol / dashboard
```

Belirli bir wire formatını anlayan tek bileşen adapter'dır. Ham frame'leri
normalize bir `LevelChange` batch'ine parse eder, frame socket'ten çıktığı anda
bir receive timestamp yakalar ve sink üzerinde `OnSnapshot` / `OnDelta` /
`OnResync` çağırır. Sink ise span'i book'a uygular ve işleme gecikmesini kaydeder.

### Book tutarlılığı

Her iki borsa da bir fiyat seviyesindeki **mutlak** bekleyen miktarı yayınlar,
ama bootstrap ve recovery şekilleri farklıdır — ve ikisi de tamamen kendi
adapter'larının içinde yaşar.

- **Binance**, `@depth` diff stream'ini açıp event'leri buffer'lar, REST
  snapshot'ı çeker, eskimiş buffer'lı event'leri (`u <= lastUpdateId`) atar ve
  `U <= lastUpdateId + 1 <= u` koşulunu sağlayan ilk event'ten itibaren uygular.
  Steady-state'te her event'in `U`'su bir önceki `u + 1`'den devam etmelidir; bir
  boşluk resync ve yeniden bootstrap tetikler.
- **Coinbase**, `level2`'ye subscribe olur, ilk `snapshot`'ı book olarak alır,
  sonra `update`'leri uygular. Bağlantının `sequence_num`'ı **global**'dir (abone
  onayını da sayar), bu yüzden süreklilik her mesaja karşı doğrulanırken book'u
  yalnızca `l2_data` mesajları değiştirir. Sıradaki bir kopukluk resync ve
  yeniden subscribe tetikler.

> Bu incelik kolayca yanlış yapılır: sequence'i kontrol etmeden önce yalnızca
> `l2_data`'ya filtrelemek sayıları kopuk gösterir ve sahte bir resync döngüsü
> üretir. TickForge her mesajı sayar ve bu bir regression testiyle korunur.

### Latency ölçümü

TickForge **iç tick-to-process gecikmesini** ölçer: bir frame'in socket'ten
okunduğu an (`Clock.NowNanos()`, monotonic) ile `OrderBook.Apply`'ın döndüğü an
arasındaki süre — tek saatli, savunulabilir bir metrik.

Borsa-yerel gecikmesini **bilerek raporlamaz**: borsa event timestamp'i ile yerel
saat senkron değildir, dolayısıyla fark gerçek ağ/işleme süresinden değil, clock
skew'den baskındır.

`Clock`, `Stopwatch.GetTimestamp()` kullanır (monotonic, mikrosaniye altı), asla
`DateTime` değil. `LatencyRecorder` bir HdrHistogram'ı sarar ve **coordinated
omission**'ı düzeltmek için `RecordValueWithExpectedInterval` kullanır — duraklayan
bir tüketicinin, duraklamanın kendi yarattığı uzun-kuyruk örneklerini
kaydedememesinden doğan yanlılık.

Tam tasarım gerekçesi için: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

---

## Performans

Lokalde tekrar üretmek için: `dotnet run -c Release --project bench/TickForge.Benchmarks`.

| Benchmark | Neyi karşılaştırır |
| --- | --- |
| Depth parsing | Elle yazılmış `Utf8JsonReader` vs source-generated DTO parsing |
| `OrderBook.ApplyDelta` | Derin bir book'a delta batch'i uygulama |
| `BookSide` decimal vs `long` tick | `decimal` doğruluğu vs scaled-integer hızı dengesi |

Elle yazılmış parser doğrudan yeniden kullanılan bir buffer'a yazar ve frame
başına **sıfır byte** allocate eder; buna karşılık source-generated yol
`string[][]` artı her price/quantity için bir `string` ürettiğinden **~5.4 KB**
harcar — mesaj başına yolun en baskın maliyeti budur.

---

## Başlangıç

**Gereksinim:** [.NET 10 SDK](https://dotnet.microsoft.com/download). Burada
kullanılan public market-data feed'leri API key istemez.

```bash
git clone https://github.com/orhanyarkin/TickForge.git
cd TickForge

dotnet build -c Release
dotnet test
```

### Konsol host

```bash
# İki borsa, BTC/USDT, saniyede bir canlı top-of-book + p50/p99:
dotnet run -c Release --project src/TickForge.App -- --exchange all --symbol BTC/USDT

# Tek borsa:
dotnet run -c Release --project src/TickForge.App -- --exchange binance --symbol BTC/USDT
```

### Web dashboard

```bash
dotnet run -c Release --project src/TickForge.Web
# http://localhost:5055 adresini aç
```

Koyu, "trading terminali" temalı bir arayüz, WebSocket üzerinden saniyede ~10
kez taze bir snapshot yayınlar: borsa başına depth bar'lı order book merdivenleri,
best bid/ask/spread/mid ve canlı latency çipleri. `GET /api/state` aynı veriyi
JSON olarak verir. Sembol ve borsalar `TICKFORGE_SYMBOL` ve `TICKFORGE_EXCHANGES`
ile ayarlanabilir.

### Docker

```bash
docker compose -f infra/docker-compose.yml up --build
# http://localhost:5055 adresini aç
```

İmaj çok aşamalıdır (SDK build → `aspnet:10.0` runtime) ve non-root kullanıcı
olarak çalışır.

---

## Proje yapısı

```
src/
  TickForge.Core/         domain model, order book, monotonic clock
  TickForge.Latency/      HdrHistogram recorder + latency kaydeden sink
  TickForge.Feeds/        Binance + Coinbase adapter'ları, WebSocket transport
  TickForge.App/          konsol host
  TickForge.Web/          ASP.NET + WebSocket dashboard
bench/
  TickForge.Benchmarks/   BenchmarkDotNet paketi
tests/
  TickForge.Tests/        xUnit
infra/
  Dockerfile, docker-compose.yml
docs/
  ARCHITECTURE.md
```

---

## Testler

```bash
dotnet test
```

Paket, bozulma olasılığı en yüksek yerleri kapsar:

- **Order book** — snapshot replace, delta apply, zero-quantity removal, best
  bid/ask seçimi, spread/mid matematiği, boş-book uç durumları, version
  monotonluğu.
- **Sequencing** — Binance gap tespiti ve bootstrap-sınır seçimi; Coinbase kopuk
  `sequence_num` (canlı testte bulunan "diğer mesaj sayacı ilerletir ama sahte
  gap üretmez" durumu dahil).
- **Latency** — bilinen bir dağılımda percentile doğruluğu ve coordinated-omission
  düzeltmesinin bir duraklamada kuyruğu gerçekten şişirmesi.
- **Parser paritesi** — elle yazılmış parser'ın source-generated yolla birebir
  aynı çıktıyı üretmesi.

Feed adapter'lar canlı socket olmadan test edilir: reconciler'lar saftır ve
snapshot kaynağı bir interface'tir, böylece sequence senaryoları in-memory veriyle
deterministik biçimde sürülür.

---

## Tasarım kararları ve dengeler

- **Price/quantity için `decimal`.** Doğru ve okunabilir, ama scaled `long`
  tick'lerden yavaş. Benchmark projesi farkı ölçer; production `decimal`'ı tutar,
  integer varyantını bir deney olarak ele alır.
- **Book tarafı başına `SortedDictionary`.** Netlik için seçildi. Yoğun book'lar
  için fiyat-indeksli düz bir dizi daha hızlıdır ve doğal bir sonraki benchmark'tır.
- **Canlı yolda source-gen JSON, yanında benchmark'lanan elle yazılmış parser.**
  Allocation-free reader testlerle eşdeğer kanıtlanır ve dokümante edilmiş fast
  path olarak tutulur.
- **Ölçmediğimiz şey.** Borsa-yerel gecikmesi. Borsa timestamp'i ile yerel saat
  senkron olmadığından, farkı raporlamak dürüst olmazdı.
