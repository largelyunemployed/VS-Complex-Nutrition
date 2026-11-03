// Shared drawing helpers for Complex Nutrition's mod-page art: parchment, ink and food icons.
// Pages build their SVG with these and are rendered to PNG by render.js.

const INK = '#3b2716';
const INK_SOFT = '#6b4a2e';
const PAPER = '#e9d8b0';

const NS = 'http://www.w3.org/2000/svg';

function el(tag, attrs, parent) {
  const e = document.createElementNS(NS, tag);
  for (const k in attrs || {}) e.setAttribute(k, attrs[k]);
  if (parent) parent.appendChild(e);
  return e;
}

// Seeded random, so every export of the art looks the same.
function rng(seed) {
  let s = seed >>> 0;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 4294967296;
  };
}

// Filters and gradients: parchment grain, ink wobble, watercolour wash, contour outline.
function addDefs(svg, w, h) {
  const defs = el('defs', {}, svg);
  defs.innerHTML = `
    <filter id="grain" x="0" y="0" width="100%" height="100%">
      <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="3" seed="7" result="n"/>
      <feColorMatrix type="matrix" values="0 0 0 0 0.36  0 0 0 0 0.25  0 0 0 0 0.12  0 0 0 0.10 0"/>
    </filter>
    <filter id="blotch" x="0" y="0" width="100%" height="100%">
      <feTurbulence type="fractalNoise" baseFrequency="0.006" numOctaves="4" seed="21" result="n"/>
      <feColorMatrix type="matrix" values="0 0 0 0 0.45  0 0 0 0 0.30  0 0 0 0 0.12  0 0 0 0.55 -0.12"/>
    </filter>
    <radialGradient id="vignette" cx="50%" cy="50%" r="72%">
      <stop offset="55%" stop-color="#7a5328" stop-opacity="0"/>
      <stop offset="100%" stop-color="#5c3a18" stop-opacity="0.55"/>
    </radialGradient>
    <filter id="ink" x="-5%" y="-5%" width="110%" height="110%">
      <feTurbulence type="fractalNoise" baseFrequency="0.035" numOctaves="2" seed="3" result="t"/>
      <feDisplacementMap in="SourceGraphic" in2="t" scale="3.2" xChannelSelector="R" yChannelSelector="G"/>
    </filter>
    <filter id="wash" x="-10%" y="-10%" width="120%" height="120%">
      <feTurbulence type="fractalNoise" baseFrequency="0.05" numOctaves="3" seed="11" result="t"/>
      <feDisplacementMap in="SourceGraphic" in2="t" scale="5" xChannelSelector="R" yChannelSelector="G" result="d"/>
      <feTurbulence type="fractalNoise" baseFrequency="0.12" numOctaves="2" seed="5" result="t2"/>
      <feColorMatrix in="t2" type="matrix" values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 -0.9 1.15" result="mottle"/>
      <feComposite in="d" in2="mottle" operator="in"/>
    </filter>
    <filter id="contour" x="-5%" y="-5%" width="110%" height="110%">
      <feMorphology in="SourceAlpha" operator="dilate" radius="2.4" result="big"/>
      <feComposite in="big" in2="SourceAlpha" operator="out" result="edge"/>
      <feFlood flood-color="${INK}"/>
      <feComposite in2="edge" operator="in" result="line"/>
      <feTurbulence type="fractalNoise" baseFrequency="0.04" numOctaves="2" seed="9" result="t"/>
      <feDisplacementMap in="line" in2="t" scale="2.5" xChannelSelector="R" yChannelSelector="G"/>
    </filter>
    <filter id="soft"><feGaussianBlur stdDeviation="6"/></filter>`;
  return defs;
}

function parchment(svg, w, h, seed) {
  const g = el('g', {}, svg);
  el('rect', { x: 0, y: 0, width: w, height: h, fill: PAPER }, g);
  el('rect', { x: 0, y: 0, width: w, height: h, filter: 'url(#blotch)' }, g);
  el('rect', { x: 0, y: 0, width: w, height: h, filter: 'url(#grain)' }, g);
  const r = rng(seed || 1);
  for (let i = 0; i < 7; i++) {
    el('circle', {
      cx: r() * w, cy: r() * h, r: 20 + r() * 70,
      fill: 'none', stroke: '#8a6232', 'stroke-opacity': 0.10 + r() * 0.08, 'stroke-width': 2 + r() * 3, filter: 'url(#soft)'
    }, g);
  }
  el('rect', { x: 0, y: 0, width: w, height: h, fill: 'url(#vignette)' }, g);
  return g;
}

// ---- food icons: ink outline over a watercolour wash, drawn in a 100x100 box centred on 0,0 ----

function icon(parent, kind, x, y, size, rot) {
  const g = el('g', { transform: `translate(${x},${y}) rotate(${rot || 0}) scale(${size / 100})` }, parent);
  const wash = el('g', { filter: 'url(#wash)', opacity: 0.85 }, g);
  const ink = el('g', { filter: 'url(#ink)', fill: 'none', stroke: INK, 'stroke-width': 4, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }, g);
  const f = FOOD[kind];
  f(wash, ink);
  return g;
}

const FOOD = {
  berry(w, k) {
    const bs = [[-14, 6, 16], [12, 10, 15], [-1, -12, 15], [2, 26, 13]];
    for (const [x, y, r] of bs) el('circle', { cx: x, cy: y, r, fill: '#4b4f8c' }, w);
    for (const [x, y, r] of bs) {
      el('circle', { cx: x, cy: y, r }, k);
      el('path', { d: `M${x - r * 0.45},${y - r * 0.2} a${r * 0.5},${r * 0.5} 0 0 1 ${r * 0.5},-${r * 0.35}`, 'stroke-width': 2.5, stroke: '#f3e7cf' }, k);
    }
    el('path', { d: 'M-1,-27 C2,-38 10,-42 20,-40 C16,-30 8,-27 -1,-27 Z', fill: '#6f8a3a' }, w);
    el('path', { d: 'M-1,-27 C2,-38 10,-42 20,-40 C16,-30 8,-27 -1,-27 Z M-1,-27 L-3,-36' }, k);
  },
  bread(w, k) {
    const d = 'M-42,14 C-46,-14 -26,-30 0,-30 C26,-30 46,-14 42,14 C40,22 -40,22 -42,14 Z';
    el('path', { d, fill: '#b9813e' }, w);
    el('path', { d: 'M-34,-6 C-20,-20 20,-20 34,-6 L34,4 L-34,4 Z', fill: '#d9a85c', opacity: 0.7 }, w);
    el('path', { d }, k);
    for (const x of [-18, 0, 18]) el('path', { d: `M${x - 8},-6 L${x + 6},-20` }, k);
  },
  meat(w, k) {
    const d = 'M-30,-8 C-38,-30 -6,-40 12,-26 C26,-16 22,6 8,14 C-6,22 -26,14 -30,-8 Z';
    el('path', { d, fill: '#a8473a' }, w);
    el('path', { d: 'M-18,-12 C-14,-22 0,-24 6,-16', stroke: '#f0d9c0', 'stroke-width': 5, fill: 'none' }, w);
    el('path', { d: 'M10,12 L30,32', stroke: '#efe4cc', 'stroke-width': 10 }, w);
    el('path', { d }, k);
    el('path', { d: 'M8,14 L26,30 M14,8 L32,26' }, k);
    el('circle', { cx: 32, cy: 34, r: 6 }, k);
    el('circle', { cx: 25, cy: 38, r: 5 }, k);
    el('path', { d: 'M-18,-12 C-14,-22 0,-24 6,-16', 'stroke-width': 2.5 }, k);
  },
  cheese(w, k) {
    const d = 'M-40,18 L-40,-2 L34,-26 L40,-6 L40,18 Z';
    el('path', { d, fill: '#e2b94a' }, w);
    el('path', { d: 'M-40,-2 L34,-26 L40,-6 Z', fill: '#f1d27a' }, w);
    el('path', { d }, k);
    el('path', { d: 'M-40,-2 L40,-6' }, k);
    for (const [x, y, r] of [[-20, 8, 5], [6, 6, 4], [24, 12, 3.5], [10, -10, 3]]) el('ellipse', { cx: x, cy: y, rx: r, ry: r * 0.8 }, k);
  },
  carrot(w, k) {
    const d = 'M-8,-22 C4,-24 12,-18 10,-8 L-18,40 C-20,43 -24,41 -23,38 Z';
    el('path', { d, fill: '#d9772e' }, w);
    el('path', { d: 'M0,-22 C-6,-36 -2,-44 6,-46 M2,-22 C6,-36 14,-40 22,-38 M-4,-21 C-14,-30 -16,-38 -12,-44', stroke: '#6f8a3a', 'stroke-width': 7, fill: 'none' }, w);
    el('path', { d }, k);
    el('path', { d: 'M0,-22 C-6,-36 -2,-44 6,-46 M2,-22 C6,-36 14,-40 22,-38 M-4,-21 C-14,-30 -16,-38 -12,-44' }, k);
    el('path', { d: 'M-2,-4 L4,-2 M-8,10 L-2,12 M-13,24 L-8,25', 'stroke-width': 3 }, k);
  },
  mushroom(w, k) {
    const cap = 'M-36,0 C-36,-26 -16,-36 0,-36 C16,-36 36,-26 36,0 C24,6 -24,6 -36,0 Z';
    el('path', { d: cap, fill: '#9a5a32' }, w);
    el('path', { d: 'M-12,4 L-14,34 C-6,38 6,38 14,34 L12,4 Z', fill: '#ece0c4' }, w);
    el('path', { d: cap }, k);
    el('path', { d: 'M-12,4 L-14,34 C-6,38 6,38 14,34 L12,4' }, k);
    for (const [x, y] of [[-16, -16], [6, -24], [18, -8]]) el('circle', { cx: x, cy: y, r: 4, fill: '#efe4cc', stroke: 'none' }, w);
  },
  fish(w, k) {
    const d = 'M-34,0 C-20,-20 14,-22 30,0 C14,22 -20,20 -34,0 Z';
    el('path', { d, fill: '#7d93a0' }, w);
    el('path', { d: 'M-30,2 C-14,14 14,14 28,2', fill: '#cfd8d6', opacity: 0.8 }, w);
    el('path', { d: 'M30,0 L46,-14 L42,0 L46,14 Z', fill: '#7d93a0' }, w);
    el('path', { d }, k);
    el('path', { d: 'M30,0 L46,-14 L42,0 L46,14 Z' }, k);
    el('circle', { cx: -20, cy: -4, r: 3, fill: INK }, k);
    el('path', { d: 'M-12,-10 C-8,-4 -8,4 -12,10', 'stroke-width': 3 }, k);
  },
  apple(w, k) {
    const d = 'M0,-22 C14,-34 38,-24 34,4 C30,30 12,36 0,30 C-12,36 -30,30 -34,4 C-38,-24 -14,-34 0,-22 Z';
    el('path', { d, fill: '#b5402f' }, w);
    el('path', { d: 'M-22,-10 C-20,-18 -12,-22 -6,-20', stroke: '#f2d6b8', 'stroke-width': 5, fill: 'none' }, w);
    el('path', { d }, k);
    el('path', { d: 'M0,-22 C0,-30 2,-36 6,-40' }, k);
    el('path', { d: 'M4,-32 C12,-42 24,-40 26,-36 C18,-30 10,-30 4,-32 Z', fill: '#6f8a3a' }, w);
    el('path', { d: 'M4,-32 C12,-42 24,-40 26,-36 C18,-30 10,-30 4,-32 Z' }, k);
  }
};
