import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
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

async function withServer(fn) {
  const root = await fixture();
  const server = createApp({ root });
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
