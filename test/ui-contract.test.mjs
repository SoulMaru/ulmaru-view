import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (name) => readFileSync(new URL(`../public/${name}`, import.meta.url), 'utf8');

test('정적 계약: 화면이 참조하는 모든 id가 HTML에 있다', () => {
  const html = read('index.html');
  const js = read('app.js');
  const ids = new Set([...js.matchAll(/querySelector\('#([a-z0-9-]+)'\)/g)].map((match) => match[1]));
  assert.ok(ids.size >= 25, `id 참조가 너무 적다: ${ids.size}`);
  for (const id of ids) assert.ok(html.includes(`id="${id}"`), `HTML에 #${id} 가 없다`);
  assert.match(html, /<script type="module" src="\/app\.js">/);
  assert.match(html, /\/styles\.css/);
  assert.match(html, /id="fs-bar"/);
  assert.match(html, /id="fs-exit"/);
  assert.match(html, /aria-label="전체화면 종료"/);
});

test('요구 1: 휠 볼륨은 5 단위이고 0~100으로 제한된다', () => {
  const html = read('index.html');
  const js = read('app.js');
  assert.match(html, /id="volume"[^>]*min="0"[^>]*max="100"[^>]*step="5"/);
  assert.match(js, /addEventListener\('wheel'/);
  assert.match(js, /wheelVolume\(state\.volume, event\.deltaY\)/);
  assert.match(js, /passive: false/);
  assert.match(js, /setVolume\(wheelVolume/);
  assert.match(js, /from '\/volume\.mjs'/);
});

test('요구 2: 전체화면 상단 X가 나타나고 누르면 종료한다', () => {
  const html = read('index.html');
  const js = read('app.js');
  const css = read('styles.css');
  assert.match(html, /class="fs-bar" id="fs-bar"/);
  assert.match(html, /class="fs-exit" id="fs-exit"/);
  assert.match(js, /function revealExit\(\)/);
  assert.match(js, /event\.clientY <= 96/);
  assert.match(js, /fsExit\.addEventListener\('click'/);
  assert.match(js, /document\.exitFullscreen/);
  assert.match(css, /\.fs-bar\.show/);
  assert.match(css, /\.stage:not\(\.is-fullscreen\) \.fs-bar/);
  assert.match(css, /\.stage\.is-fullscreen/);
});

test('요구 3: Enter는 전체화면 토글, Esc는 전체화면 종료', () => {
  const js = read('app.js');
  assert.match(js, /event\.key === 'Enter'/);
  assert.match(js, /event\.key === 'Escape'/);
  assert.match(js, /toggleFullscreen\(\)/);
  assert.match(js, /requestFullscreen\(\)/);
  assert.match(js, /exitFullscreen\(\)/);
  assert.match(js, /if \(isFullscreen\(\)\)/);
});

test('경계: 화면은 로컬 API만 쓰고 외부 스크립트를 부르지 않는다', () => {
  const js = read('app.js');
  const html = read('index.html');
  assert.doesNotMatch(html, /https?:\/\//, '외부 리소스를 부르지 않는다');
  assert.doesNotMatch(js, /method:\s*['"]POST['"]/);
  assert.match(js, /fetch\(url, \{ cache: 'no-store' \}\)/);
  assert.match(js, /window\.__ULMARU_VIEW__/);
});
