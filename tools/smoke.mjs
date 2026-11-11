// 얼마루 뷰 스모크 — 실제 Chromium으로 목록·재생·휠 볼륨·전체화면 상단 X를 확인한다.
// 실행: node tools/smoke.mjs [--root <미디어 폴더>] [--out /tmp/opencode]
// playwright-core가 없으면 건너뛴다(개발 PC 전용 선택 도구).
import { createRequire } from 'node:module';
import { existsSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { homedir } from 'node:os';

import { startServer } from '../server.mjs';

const args = process.argv.slice(2);
const flag = (name, fallback) => {
  const index = args.indexOf(`--${name}`);
  return index >= 0 && args[index + 1] ? args[index + 1] : fallback;
};

const outDir = flag('out', '/tmp/opencode');
mkdirSync(outDir, { recursive: true });

const roots = [
  flag('root', ''),
  process.env.ULMARU_SMOKE_ROOT,
  '/mnt/c/AI_RUNTIME_PC1/04_CONTENT_STUDIO/AUDIO/업로드',
  '/mnt/c/AI_RUNTIME_PC1/04_CONTENT_STUDIO',
  join(homedir(), 'soulmaru-2', 'downloads'),
].filter(Boolean);
const root = roots.find((candidate) => existsSync(candidate)) || process.cwd();

const require = createRequire(import.meta.url);
const candidates = [
  process.env.PLAYWRIGHT_CORE,
  join(homedir(), '.local/npm/lib/node_modules/@playwright/mcp/node_modules/playwright-core'),
  'playwright-core',
].filter(Boolean);

let chromium = null;
for (const candidate of candidates) {
  try { ({ chromium } = require(candidate)); break; } catch { /* 다음 후보 */ }
}
if (!chromium) {
  console.log('SKIP: playwright-core를 찾지 못했습니다. (PLAYWRIGHT_CORE 환경변수로 지정)');
  process.exit(0);
}

const executablePath = [
  process.env.CHROMIUM_PATH,
  join(homedir(), '.cache/ms-playwright/chromium-1254/chrome-linux64/chrome'),
  join(homedir(), '.cache/ms-playwright/chromium-1254/chrome-linux/chrome'),
].filter(Boolean).find((path) => existsSync(path));

const checks = [];
const check = (name, ok, detail = '') => {
  checks.push({ name, ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? ` — ${detail}` : ''}`);
};

const serverInfo = await startServer({ port: 0, host: '127.0.0.1', root });
const browser = await chromium.launch({
  executablePath,
  headless: true,
  args: ['--no-sandbox', '--autoplay-policy=no-user-gesture-required'],
});
const page = await browser.newPage({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
const errors = [];
page.on('console', (message) => { if (message.type() === 'error') errors.push(`console: ${message.text()}`); });
page.on('pageerror', (error) => errors.push(`pageerror: ${error.message}`));

try {
  console.log(`미디어 폴더: ${root}`);
  await page.goto(serverInfo.url, { waitUntil: 'domcontentloaded', timeout: 45_000 });
  await page.waitForFunction(() => window.__ULMARU_VIEW__ && window.__ULMARU_VIEW__.state().total > 0, null, { timeout: 60_000 });
  await page.waitForTimeout(600);

  const stats = await page.evaluate(() => window.__ULMARU_VIEW__.state());
  check('미디어 목록 탐색', stats.total > 0 && stats.loaded > 0, `total=${stats.total} loaded=${stats.loaded}`);
  check('종류별 개수', stats.counts.video + stats.counts.audio + stats.counts.image === stats.total, JSON.stringify(stats.counts));
  check('기본 볼륨 5 단위', stats.step === 5, `step=${stats.step}`);

  const rows = await page.locator('#media-list .media-item').count();
  check('목록 렌더', rows > 0, `rows=${rows}`);

  const picked = await page.evaluate(() => {
    const items = window.__ULMARU_VIEW__.items();
    const item = items.find((entry) => entry.kind === 'video') || items.find((entry) => entry.kind === 'audio') || items[0];
    return item && window.__ULMARU_VIEW__.select(item.id) ? item : null;
  });
  check('미디어 선택', Boolean(picked), picked ? `${picked.kind} · ${picked.name}` : '없음');
  await page.waitForTimeout(700);
  const now = await page.evaluate(() => window.__ULMARU_VIEW__.state().current);
  check('재생기 연결', now && now.id === picked?.id, now?.name || '없음');

  const box = await page.locator('#stage').boundingBox();
  await page.mouse.move(box.x + box.width * 0.5, box.y + box.height * 0.5);
  const before = await page.evaluate(() => window.__ULMARU_VIEW__.volume());
  await page.mouse.wheel(0, -120);
  await page.waitForTimeout(150);
  const up = await page.evaluate(() => window.__ULMARU_VIEW__.volume());
  await page.mouse.wheel(0, 120);
  await page.waitForTimeout(150);
  const down = await page.evaluate(() => window.__ULMARU_VIEW__.volume());
  check('휠 위로 볼륨 +5', up === Math.min(100, before + 5), `${before} → ${up}`);
  check('휠 아래로 볼륨 -5', down === Math.max(0, up - 5), `${up} → ${down}`);
  const label = await page.locator('#volume-value').innerText();
  check('볼륨 표시 갱신', label === `${down}%`, label);

  await page.screenshot({ path: join(outDir, 'ulmaru-view-library.png') });

  // Enter 전체화면
  await page.keyboard.press('Enter');
  await page.waitForTimeout(600);
  const entered = await page.evaluate(() => window.__ULMARU_VIEW__.isFullscreen());
  check('Enter 전체화면 진입', entered === true, `fullscreen=${entered}`);

  if (entered) {
    await page.mouse.move(box.x + box.width * 0.5, 6);
    await page.waitForTimeout(250);
    const visible = await page.evaluate(() => window.__ULMARU_VIEW__.exitVisible());
    check('상단으로 마우스 → X 표시', visible === true, `visible=${visible}`);
    await page.screenshot({ path: join(outDir, 'ulmaru-view-fullscreen.png') });

    await page.click('#fs-exit');
    await page.waitForTimeout(500);
    const exited = await page.evaluate(() => window.__ULMARU_VIEW__.isFullscreen());
    check('X 클릭 → 전체화면 종료', exited === false, `fullscreen=${exited}`);

    await page.keyboard.press('Enter');
    await page.waitForTimeout(500);
    const again = await page.evaluate(() => window.__ULMARU_VIEW__.isFullscreen());
    check('Enter 다시 전체화면', again === true, `fullscreen=${again}`);
    await page.keyboard.press('Escape');
    await page.waitForTimeout(500);
    const escaped = await page.evaluate(() => window.__ULMARU_VIEW__.isFullscreen());
    check('Esc → 전체화면 종료', escaped === false, `fullscreen=${escaped}`);
  } else {
    check('상단으로 마우스 → X 표시', false, '전체화면 진입 실패로 확인 불가');
    check('X 클릭 → 전체화면 종료', false, '전체화면 진입 실패로 확인 불가');
    check('Enter 다시 전체화면', false, '전체화면 진입 실패로 확인 불가');
    check('Esc → 전체화면 종료', false, '전체화면 진입 실패로 확인 불가');
  }

  const fatalErrors = errors.filter((text) => !/favicon|net::ERR_/.test(text));
  check('브라우저 오류 없음', fatalErrors.length === 0, fatalErrors.slice(0, 2).join(' | '));

  console.log(`\n${checks.filter((item) => item.ok).length}/${checks.length} 통과`);
  if (checks.some((item) => !item.ok)) process.exitCode = 1;
} catch (error) {
  console.error('SMOKE ERROR', error);
  await page.screenshot({ path: join(outDir, 'ulmaru-view-error.png') }).catch(() => {});
  process.exitCode = 1;
} finally {
  await browser.close();
  await new Promise((resolve) => serverInfo.server.close(resolve));
}
