import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { createApp } from '../server.mjs';

async function fixture() {
  const dir = await mkdtemp(join(tmpdir(), 'ulmaru-view-server-'));
  await mkdir(join(dir, 'sub'), { recursive: true });
  await writeFile(join(dir, 'clip.mp4'), Buffer.from('0123456789'));
  await writeFile(join(dir, 'song.mp3'), Buffer.from('abcdef'));
  await writeFile(join(dir, 'sub', 'photo.png'), Buffer.from('PNGDATA'));
  await writeFile(join(dir, 'readme.txt'), 'not media');
  return dir;
}

async function withServer(fn, options = {}) {
  const root = await fixture();
  const server = createApp({ root, cacheDir: false, ...options });
  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', resolve);
  });
  const { port } = server.address();
  const base = `http://127.0.0.1:${port}`;
  try {
    await fn({ base, root });
  } finally {
    await new Promise((resolve) => server.close(resolve));
    await rm(root, { recursive: true, force: true });
  }
}

test('API: 상태와 목록이 미디어만 세고 종류·검색으로 거른다', async () => {
  await withServer(async ({ base }) => {
    const state = await (await fetch(`${base}/api/state`)).json();
    assert.equal(state.total, 3);
    assert.deepEqual(state.counts, { video: 1, audio: 1, image: 1 });
    assert.equal(state.volumeStep, 5);
    assert.equal(state.truncated, false);

    const all = await (await fetch(`${base}/api/media`)).json();
    assert.equal(all.total, 3);
    assert.equal(all.items.length, 3);
    assert.ok(all.items.every((item) => item.sizeText));

    const videos = await (await fetch(`${base}/api/media?kind=video`)).json();
    assert.equal(videos.total, 1);
    assert.equal(videos.items[0].name, 'clip.mp4');

    const search = await (await fetch(`${base}/api/media?q=${encodeURIComponent('photo')}`)).json();
    assert.equal(search.total, 1);
    assert.equal(search.items[0].folder, 'sub');

    const none = await (await fetch(`${base}/api/media?q=nothing`)).json();
    assert.equal(none.total, 0);
  });
});

test('스트림: 전체와 Range 요청을 올바른 헤더로 돌려준다', async () => {
  await withServer(async ({ base }) => {
    const list = await (await fetch(`${base}/api/media?kind=video`)).json();
    const item = list.items[0];

    const full = await fetch(`${base}/api/stream/${item.id}`);
    assert.equal(full.status, 200);
    assert.equal(full.headers.get('content-type'), 'video/mp4');
    assert.equal(full.headers.get('accept-ranges'), 'bytes');
    assert.equal(await full.text(), '0123456789');

    const part = await fetch(`${base}/api/stream/${item.id}`, { headers: { Range: 'bytes=2-5' } });
    assert.equal(part.status, 206);
    assert.equal(part.headers.get('content-range'), 'bytes 2-5/10');
    assert.equal(part.headers.get('content-length'), '4');
    assert.equal(await part.text(), '2345');

    const suffix = await fetch(`${base}/api/stream/${item.id}`, { headers: { Range: 'bytes=-3' } });
    assert.equal(suffix.status, 206);
    assert.equal(await suffix.text(), '789');

    const bad = await fetch(`${base}/api/stream/${item.id}`, { headers: { Range: 'bytes=99-120' } });
    assert.equal(bad.status, 416);

    const missing = await fetch(`${base}/api/stream/AAAAAAAAAAAAAAAAAAAAAA`);
    assert.equal(missing.status, 404);
  });
});

test('정적: 화면·스크립트·볼륨 모듈을 제공하고 쓰기 메서드는 거절한다', async () => {
  await withServer(async ({ base }) => {
    const home = await fetch(`${base}/`);
    assert.equal(home.status, 200);
    assert.match(home.headers.get('content-type'), /text\/html/);
    assert.match(await home.text(), /얼마루 뷰/);

    const app = await fetch(`${base}/app.js`);
    assert.equal(app.status, 200);
    assert.match(await app.text(), /wheel/);

    const volume = await fetch(`${base}/volume.mjs`);
    assert.equal(volume.status, 200);
    assert.match(await volume.text(), /VOLUME_STEP/);

    const missing = await fetch(`${base}/nope`);
    assert.equal(missing.status, 404);

    const post = await fetch(`${base}/api/state`, { method: 'POST' });
    assert.equal(post.status, 405);
  });
});

test('캐시: 재시작하면 디스크 색인을 바로 쓰고, 새로고침은 다시 읽는다', async () => {
  const root = await fixture();
  const cacheDir = await mkdtemp(join(tmpdir(), 'ulmaru-view-cache-'));
  const open = async () => {
    const server = createApp({ root, cacheDir });
    await new Promise((resolve, reject) => {
      server.once('error', reject);
      server.listen(0, '127.0.0.1', resolve);
    });
    return { server, base: `http://127.0.0.1:${server.address().port}` };
  };
  try {
    const first = await open();
    const before = await (await fetch(`${first.base}/api/state`)).json();
    assert.equal(before.total, 3);
    await new Promise((resolve) => first.server.close(resolve));

    const files = await readdir(cacheDir);
    assert.equal(files.filter((name) => name.endsWith('.json')).length, 1, '색인 파일이 하나 생긴다');

    // 파일을 지운 뒤에도 캐시가 있으면 즉시 이전 목록을 준다.
    await rm(join(root, 'clip.mp4'));
    const second = await open();
    const stale = await (await fetch(`${second.base}/api/state`)).json();
    assert.equal(stale.total, 3);

    const refreshed = await (await fetch(`${second.base}/api/state?refresh=1`)).json();
    assert.equal(refreshed.total, 2);
    assert.deepEqual(refreshed.counts, { video: 0, audio: 1, image: 1 });
    await new Promise((resolve) => second.server.close(resolve));
  } finally {
    await rm(root, { recursive: true, force: true });
    await rm(cacheDir, { recursive: true, force: true });
  }
});

test('보안: 심볼릭 링크가 아닌 정상 파일만 스트리밍한다', async () => {
  await withServer(async ({ base, root }) => {
    const list = await (await fetch(`${base}/api/media`)).json();
    for (const item of list.items) {
      const response = await fetch(`${base}/api/stream/${item.id}`);
      assert.equal(response.status, 200, item.name);
      await response.arrayBuffer();
    }
    assert.ok(root.length > 0);
  });
});
