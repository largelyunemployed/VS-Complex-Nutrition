// Shared drawing helpers for the art pages, in the style of Vintage Story's own UI:
// brown pixel-noise dialog panels, peach inventory slots, beige Lato text and 16x16 pixel sprites.
// Colours were sampled from a 1.22 screenshot (character creation dialog and hotbar).

const NS = 'http://www.w3.org/2000/svg';

const VS = {
  panel: ['#4a3c2e', '#4f4033', '#554536', '#5b4a3b', '#5f4c42'],
  inset: ['#352b21', '#3a2f24', '#3c3125', '#41352a', '#2f261d'],
  edgeDark: '#251c15',
  edgeLight: '#7a6450',
  text: '#e9ddce',
  textSoft: '#b9a58f',
  slot: '#fde0c0',
  slotShade: '#f2cfaa',
  slotBorder: '#29221c',
  slotIcon: '#c9a27c',
  slotNumber: '#9a7f66',
  selected: '#62c5db',
  done: '#8fa040',
  health: '#b0171b',
  satiety: '#7b8536',
};

function el(tag, attrs, parent) {
  const e = document.createElementNS(NS, tag);
  for (const k in attrs) e.setAttribute(k, attrs[k]);
  if (parent) parent.appendChild(e);
  return e;
}

// Small seeded random generator, so every render comes out identical.
function rng(seed) {
  let s = seed >>> 0 || 1;
  return () => {
    s ^= s << 13; s >>>= 0;
    s ^= s >> 17;
    s ^= s << 5; s >>>= 0;
    return s / 4294967296;
  };
}

// A rectangle filled with square texels picked at random from `colors` (first colour weighted up).
function noiseRect(parent, x, y, w, h, t, colors, seed) {
  const g = el('g', { 'shape-rendering': 'crispEdges' }, parent);
  el('rect', { x, y, width: w, height: h, fill: colors[0] }, g);
  const r = rng(seed);
  for (let ty = 0; ty < h; ty += t) {
    for (let tx = 0; tx < w; tx += t) {
      const c = colors[Math.floor(r() * colors.length)];
      if (c === colors[0]) continue;
      el('rect', { x: x + tx, y: y + ty, width: Math.min(t, w - tx), height: Math.min(t, h - ty), fill: c }, g);
    }
  }
  return g;
}

// The game's dialog panel: brown noise with a dark outline and a faint light top edge.
function panel(parent, x, y, w, h, seed, t = 10) {
  noiseRect(parent, x, y, w, h, t, VS.panel, seed);
  el('rect', { x: x + 1.5, y: y + 1.5, width: w - 3, height: h - 3, fill: 'none', stroke: VS.edgeDark, 'stroke-width': 3 }, parent);
  el('line', { x1: x + 4, y1: y + 4.5, x2: x + w - 4, y2: y + 4.5, stroke: VS.edgeLight, 'stroke-width': 1.5, 'stroke-opacity': 0.6 }, parent);
}

// A sunken area inside a panel, like the character preview in the skin dialog.
function inset(parent, x, y, w, h, seed, t = 10) {
  noiseRect(parent, x, y, w, h, t, VS.inset, seed);
  el('rect', { x: x + 1, y: y + 1, width: w - 2, height: h - 2, fill: 'none', stroke: VS.edgeDark, 'stroke-width': 2 }, parent);
  el('line', { x1: x, y1: y + h + 1, x2: x + w, y2: y + h + 1, stroke: VS.edgeLight, 'stroke-width': 2, 'stroke-opacity': 0.5 }, parent);
}

// A hotbar / inventory slot. opts.ring: highlight colour drawn round the slot; opts.number: corner label.
function slot(parent, x, y, s, opts = {}) {
  const g = el('g', {}, parent);
  if (opts.ring) {
    el('rect', { x: x - 7, y: y - 7, width: s + 14, height: s + 14, fill: opts.ring, rx: 2 }, g);
  }
  el('rect', { x, y, width: s, height: s, fill: VS.slotBorder, rx: 3 }, g);
  const b = Math.max(4, s * 0.055);
  el('rect', { x: x + b, y: y + b, width: s - 2 * b, height: s - 2 * b, fill: VS.slot }, g);
  // soft inner shade along the top and left, as in game
  el('rect', { x: x + b, y: y + b, width: s - 2 * b, height: b * 0.9, fill: VS.slotShade }, g);
  el('rect', { x: x + b, y: y + b, width: b * 0.9, height: s - 2 * b, fill: VS.slotShade }, g);
  if (opts.number != null) {
    const n = el('text', { x: x + s - b - s * 0.06, y: y + b + s * 0.2, 'text-anchor': 'end', 'font-family': 'Lato', 'font-weight': 400, 'font-size': s * 0.2, fill: VS.slotNumber }, g);
    n.textContent = opts.number;
  }
  return g;
}

// Text with the slight dark drop shadow the game puts under its UI text.
function text(parent, x, y, str, attrs) {
  const a = Object.assign({ x, y, 'font-family': 'Lato', fill: VS.text }, attrs);
  const sh = el('text', Object.assign({}, a, { x: x + 2, y: y + 2, fill: '#000', 'fill-opacity': 0.45 }), parent);
  sh.textContent = str;
  const t = el('text', a, parent);
  t.textContent = str;
  return t;
}

// ---- 16x16 pixel sprites. '.' is transparent; other characters index the sprite's palette. ----
const OUTLINE = '#2a1d14';
const SPRITES = {
  apple: {
    pal: { k: OUTLINE, r: '#c23b2a', h: '#ef8b72', d: '#8c2a1e', s: '#5a3a1e', g: '#6f9a3a', G: '#4f7428' },
    rows: [
      '................',
      '.......ks.......',
      '.......ks.kkk...',
      '.......kskgGk...',
      '...kkkkkskkk....',
      '..krrrrrrrrrk...',
      '.krhhrrrrrrrrk..',
      '.krhrrrrrrrrrk..',
      'krhrrrrrrrrrrdk.',
      'krrrrrrrrrrrrdk.',
      'krrrrrrrrrrrddk.',
      '.krrrrrrrrrrdk..',
      '.krrrrrrrrrddk..',
      '..krrrdkkrddk...',
      '...kkkk..kkk....',
      '................',
    ],
  },
  bread: {
    pal: { k: OUTLINE, b: '#c98a3e', h: '#ecc17a', d: '#8f5a26', c: '#a96d30' },
    rows: [
      '................',
      '................',
      '................',
      '.....kkkkkk.....',
      '...kkbbbbbbkk...',
      '..kbbbhbbbhbbk..',
      '.kbbbhbbbhbbbbk.',
      '.kbbhbbbhbbbbbk.',
      'kbbbbbbbbbbbbbbk',
      'kbbbbbbbbbbbbbck',
      'kcbbbbbbbbbbbcck',
      'kccbbbbbbbbbccdk',
      'kdcccccccccccddk',
      '.kddddddddddddk.',
      '..kkkkkkkkkkkk..',
      '................',
    ],
  },
  carrot: {
    pal: { k: OUTLINE, o: '#e07a26', h: '#f6a85a', d: '#a9521a', g: '#6f9a3a', G: '#4f7428' },
    rows: [
      '...........g..g.',
      '..........gG.gG.',
      '...........gGgG.',
      '.........kkkGG..',
      '........kohokG..',
      '.......kohooko..',
      '......kohooodk..',
      '.....kohooodk...',
      '....kohooodk....',
      '....koooodk.....',
      '...kohoodk......',
      '...kooodk.......',
      '..koodkk........',
      '..kodk..........',
      '.kdk............',
      '.k..............',
    ],
  },
  meat: {
    pal: { k: OUTLINE, m: '#c4503c', h: '#e88a72', d: '#8a3326', f: '#f0dcc4', w: '#efe6d2', W: '#bfb39c' },
    rows: [
      '................',
      '...kkkkkk.......',
      '..kfmmmmmkk.....',
      '.kfmhhmmmmmk....',
      '.kfhmmmmmmmmk...',
      'kfmmmmmmmmmmk...',
      'kfmmmmmmmmmdk...',
      'kfmmmmmmmmddk...',
      '.kfmmmmmmddk....',
      '.kffmmmdddk.....',
      '..kkffddkwk.....',
      '....kkkkwWk.kk..',
      '........kwWkwwk.',
      '........kwwwwWk.',
      '.......kwwwWWk..',
      '........kkkkk...',
    ],
  },
  fish: {
    pal: { k: OUTLINE, b: '#7d9fb3', h: '#b9d0dc', d: '#4e6b7e', l: '#d9e3e6', e: '#111111', f: '#5f8296' },
    rows: [
      '................',
      '................',
      '................',
      '.....kkkkkk.....',
      '...kkbbhbbbkk..k',
      '..kbbhbbbbbbbkkk',
      '.kbekbbbbbbbbbfk',
      'kbbbbbbbbbbbbbfk',
      'kbbbbbbbbbbbbbfk',
      '.kllllllllldbbfk',
      '..kllllllldddkkk',
      '...kkdddddddk..k',
      '.....kkkkkkk....',
      '................',
      '................',
      '................',
    ],
  },
  cheese: {
    pal: { k: OUTLINE, y: '#f3d35a', t: '#fbe78f', o: '#d9ac2e', h: '#a97e1e' },
    rows: [
      '................',
      '................',
      '................',
      '..........kk....',
      '........kkttk...',
      '......kkttttk...',
      '....kkttttttk...',
      '..kkttttttttyk..',
      '.kyyyyyyyyyyyk..',
      '.kyyhyyyyyhyok..',
      '.kyhhyyyyyyyok..',
      '.kyyyyyhhyyyok..',
      '.kyyyyyhhyyook..',
      '.kooooooooooook.',
      '..kkkkkkkkkkkkk.',
      '................',
    ],
  },
  mushroom: {
    pal: { k: OUTLINE, c: '#9c6436', h: '#c78f58', d: '#6e4122', s: '#eadfc4', S: '#c8b994', u: '#d8c6a0' },
    rows: [
      '................',
      '................',
      '.....kkkkkk.....',
      '...kkcchhcckk...',
      '..kcchhccccdck..',
      '.kcchccccccddck.',
      '.kcccccccccddck.',
      'kccccccccccdddck',
      'kuuuuuuuuuuuuuuk',
      '.kkkksssSSkkkkk.',
      '....ksssSSk.....',
      '....kssssSk.....',
      '....kssssSk.....',
      '...ksssssSSk....',
      '...kkkkkkkkk....',
      '................',
    ],
  },
  berries: {
    pal: { k: OUTLINE, b: '#3f4f9a', h: '#8c9ad6', d: '#28306a', g: '#6f9a3a', G: '#4f7428', s: '#4f3a20' },
    rows: [
      '................',
      '.........ss.....',
      '........s..gg...',
      '.......s..gGGg..',
      '..kkk.s.kkkgGg..',
      '.kbhbkskbhbk.g..',
      '.kbbbksbbbbdk...',
      '.kbbdk.kbbddk...',
      '..kkkkkkkkkk....',
      '....kbhbbkbhbk..',
      '...kbhbbbkbbbdk.',
      '...kbbbbdkbbddk.',
      '...kbbbddkbddk..',
      '....kkkk..kkk...',
      '................',
      '................',
    ],
  },
};

function sprite(parent, name, x, y, px) {
  const s = SPRITES[name];
  const g = el('g', { 'shape-rendering': 'crispEdges' }, parent);
  s.rows.forEach((row, ry) => {
    for (let rx = 0; rx < row.length; rx++) {
      const c = row[rx];
      if (c === '.') continue;
      el('rect', { x: x + rx * px, y: y + ry * px, width: px, height: px, fill: s.pal[c] || '#ff00ff' }, g);
    }
  });
  return g;
}
