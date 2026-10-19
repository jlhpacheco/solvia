// Render the app's S/arrow brand as a circular PNG accepted by PayPal.
// This exports the vector source; no external image service is involved.
const { chromium } = require('@playwright/test');
const { readFileSync } = require('node:fs');
const path = require('node:path');
(async () => {
  const source = path.resolve(__dirname, '../src/Solvia/wwwroot/img/solvia-avatar.svg');
  const destination = path.resolve(__dirname, '../src/Solvia/wwwroot/img/solvia-avatar.png');
  const browser = await chromium.launch();
  try {
    const page = await browser.newPage({ viewport: { width: 512, height: 512 }, deviceScaleFactor: 1 });
    await page.setContent('<style>html,body{margin:0;background:transparent}svg{display:block}</style>' + readFileSync(source, 'utf8'));
    await page.screenshot({ path: destination, omitBackground: true });
    console.log('Exported 512px circular Solvia avatar.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
