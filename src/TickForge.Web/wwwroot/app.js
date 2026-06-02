'use strict';

const DEPTH = 12;
const exEl = document.getElementById('exchanges');
const statusEl = document.getElementById('status');
const symbolEl = document.getElementById('symbol');
const updatedEl = document.getElementById('updated');
const cards = new Map();

const priceFmt = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const sizeFmt = new Intl.NumberFormat('en-US', { maximumFractionDigits: 4 });

const fmtPrice = (n) => (n == null ? '—' : priceFmt.format(n));
const fmtSize = (n) => (n == null ? '' : sizeFmt.format(n));
const fmtLat = (n) => (n == null ? '—' : n >= 1000 ? (n / 1000).toFixed(2) + 'ms' : n.toFixed(1));

function makeRow(side) {
  const row = document.createElement('div');
  row.className = 'row ' + side + ' empty';
  const bar = document.createElement('div');
  bar.className = 'bar';
  const price = document.createElement('span');
  price.className = 'price';
  const size = document.createElement('span');
  size.className = 'size';
  row.append(bar, price, size);
  return { row, bar, price, size };
}

function makeCard(name) {
  const card = document.createElement('section');
  card.className = 'card';
  card.innerHTML = `
    <div class="card-head"><span class="ex"></span><span class="ver"></span></div>
    <div class="tob">
      <div class="cell bid"><span class="k">bid</span><span class="v bb">—</span></div>
      <div class="cell"><span class="k">mid</span><span class="v mv">—</span></div>
      <div class="cell ask"><span class="k">ask</span><span class="v ba">—</span></div>
    </div>
    <div class="ladder asks"><div class="rows asks-rows"></div></div>
    <div class="mid"><span>spread</span><span class="spread">—</span></div>
    <div class="ladder bids"><div class="rows bids-rows"></div></div>
    <div class="lat"></div>`;

  card.querySelector('.ex').textContent = name;
  const asksRows = card.querySelector('.asks-rows');
  const bidsRows = card.querySelector('.bids-rows');
  const askRowEls = [];
  const bidRowEls = [];
  for (let i = 0; i < DEPTH; i++) {
    const r = makeRow('ask');
    askRowEls.push(r);
    asksRows.append(r.row);
  }
  for (let i = 0; i < DEPTH; i++) {
    const r = makeRow('bid');
    bidRowEls.push(r);
    bidsRows.append(r.row);
  }

  const latEl = card.querySelector('.lat');
  const chips = {};
  for (const key of ['p50', 'p90', 'p99', 'p999']) {
    const chip = document.createElement('div');
    chip.className = 'chip ' + key;
    chip.innerHTML = `<span class="k">${key === 'p999' ? 'p99.9' : key}</span><span class="v">—</span>`;
    latEl.append(chip);
    chips[key] = chip.querySelector('.v');
  }

  exEl.append(card);
  return {
    ver: card.querySelector('.ver'),
    bb: card.querySelector('.bb'),
    ba: card.querySelector('.ba'),
    mv: card.querySelector('.mv'),
    spread: card.querySelector('.spread'),
    askRowEls,
    bidRowEls,
    chips,
  };
}

function updateRows(rowEls, levels, maxQty) {
  for (let i = 0; i < rowEls.length; i++) {
    const r = rowEls[i];
    const lv = levels[i];
    if (!lv) {
      r.row.classList.add('empty');
      r.bar.style.width = '0%';
      r.price.textContent = '';
      r.size.textContent = '';
      continue;
    }
    r.row.classList.remove('empty');
    r.price.textContent = fmtPrice(lv.price);
    r.size.textContent = fmtSize(lv.quantity);
    // Square-root scaling so a single outsized order does not flatten the rest;
    // small levels stay visible while the largest still maps to full width.
    const ratio = maxQty > 0 ? Math.sqrt(lv.quantity / maxQty) : 0;
    r.bar.style.width = ratio * 100 + '%';
  }
}

function update(state) {
  symbolEl.textContent = state.symbol;
  for (const ex of state.exchanges) {
    let c = cards.get(ex.name);
    if (!c) {
      c = makeCard(ex.name);
      cards.set(ex.name, c);
    }

    c.ver.textContent = 'v' + ex.version;
    c.bb.textContent = fmtPrice(ex.bestBid);
    c.ba.textContent = fmtPrice(ex.bestAsk);
    c.mv.textContent = fmtPrice(ex.mid);
    c.spread.textContent = ex.spread == null ? '—' : fmtPrice(ex.spread);

    let maxQty = 0;
    for (const l of ex.bids) if (l.quantity > maxQty) maxQty = l.quantity;
    for (const l of ex.asks) if (l.quantity > maxQty) maxQty = l.quantity;

    updateRows(c.askRowEls, ex.asks, maxQty);
    updateRows(c.bidRowEls, ex.bids, maxQty);

    const lat = ex.latency;
    c.chips.p50.textContent = fmtLat(lat.p50);
    c.chips.p90.textContent = fmtLat(lat.p90);
    c.chips.p99.textContent = fmtLat(lat.p99);
    c.chips.p999.textContent = fmtLat(lat.p999);
  }
  updatedEl.textContent = new Date(state.timestamp).toLocaleTimeString();
}

function setStatus(on) {
  statusEl.className = 'status ' + (on ? 'on' : 'off');
  statusEl.innerHTML = '<i></i>' + (on ? 'live' : 'disconnected');
}

function connect() {
  const proto = location.protocol === 'https:' ? 'wss' : 'ws';
  const ws = new WebSocket(`${proto}://${location.host}/ws`);
  ws.onopen = () => setStatus(true);
  ws.onmessage = (e) => {
    try {
      update(JSON.parse(e.data));
    } catch (err) {
      console.error('bad frame', err);
    }
  };
  ws.onclose = () => {
    setStatus(false);
    setTimeout(connect, 1000);
  };
  ws.onerror = () => {
    try { ws.close(); } catch { /* ignore */ }
  };
}

connect();
