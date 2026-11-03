// Renders the art pages to PNG with headless Chromium (Playwright).
// Usage: node art/render.js            -> writes art/out/*.png and ComplexNutrition/modicon.png
// Thumbnails are drawn at 1024 px and scaled down by Chromium (deviceScaleFactor) for clean edges.
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright');

const here = __dirname;
const out = path.join(here, 'out');
fs.mkdirSync(out, { recursive: true });

const jobs = [
  { page: 'thumbnail.html', w: 1024, h: 1024, scale: 1, file: 'thumbnail-1024.png' },
  { page: 'thumbnail.html', w: 1024, h: 1024, scale: 0.5, file: 'thumbnail-512.png' },
  { page: 'thumbnail.html', w: 1024, h: 1024, scale: 0.25, file: path.join('..', '..', 'ComplexNutrition', 'modicon.png') },
  { page: 'roadmap.html', w: 1920, h: 720, scale: 1, file: 'roadmap-1920.png' },
];

(async () => {
  const browser = await chromium.launch();
  for (const j of jobs) {
    const page = await browser.newPage({ viewport: { width: j.w, height: j.h }, deviceScaleFactor: j.scale });
    await page.goto('file://' + path.join(here, j.page));
    await page.waitForFunction(() => document.body.dataset.ready === '1');
    await page.evaluate(() => document.fonts.ready);
    const target = path.join(out, j.file);
    await page.locator('svg#art').screenshot({ path: target });
    console.log('wrote', path.normalize(target));
    await page.close();
  }
  await browser.close();
})();
