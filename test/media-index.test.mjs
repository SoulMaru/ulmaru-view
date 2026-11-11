import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, utimes, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import {
  kindOf, mediaId, resolveMediaPath, folderOf, formatBytes,
  scanMedia, filterMedia, pageMedia, DEFAULT_LIMIT, MAX_LIMIT,
} from '../src/media-index.mjs';
import { VOLUME_STEP, clampVolume, stepVolume, wheelVolume } from '../public/volume.mjs';

test('종류: 확장자로 영상·오디오·이미지를 가른다', () => {
  assert.equal(kindOf('a.mp4'), 'video');
  assert.equal(kindOf('a.WEBM'), 'video');
  assert.equal(kindOf('a.mkv'), 'video');
  assert.equal(kindOf('a.mp3'), 'audio');
  assert.equal(kindOf('a.WAV'), 'audio');
  assert.equal(kindOf('a.flac'), 'audio');
  assert.equal(kindOf('a.png'), 'image');
  assert.equal(kindOf('a.JPEG'), 'image');
  assert.equal(kindOf('a.txt'), null);
  assert.equal(kindOf(''), null);
  assert.equal(kindOf(null), null);
  assert.equal(kindOf('.mp4'), 'video');
});

test('볼륨: 0~100 정수, 5 단위로 오르내린다', () => {
  assert.equal(VOLUME_STEP, 5);
  assert.equal(clampVolume(80), 80);
  assert.equal(clampVolume(-10), 0);
  assert.equal(clampVolume(130), 100);
  assert.equal(clampVolume('65'), 65);
  assert.equal(clampVolume('abc'), 0);
  assert.equal(clampVolume(64.6), 65);

  assert.equal(stepVolume(80, 1), 85);
  assert.equal(stepVolume(80, -1), 75);
  assert.equal(stepVolume(98, 1), 100);
  assert.equal(stepVolume(2, -1), 0);
  assert.equal(stepVolume(80, 0), 80);
  assert.equal(stepVolume(80, 'x'), 80);
});

test('볼륨: 휠 위는 올리고 아래는 내리며 0/100에서 멈춘다', () => {
  assert.equal(wheelVolume(50, -120), 55);
  assert.equal(wheelVolume(50, 120), 45);
  assert.equal(wheelVolume(100, -1), 100);
  assert.equal(wheelVolume(0, 1), 0);
  assert.equal(wheelVolume(50, 0), 50);
  assert.equal(wheelVolume(50, 'x'), 50);
});

test('id: 같은 경로는 같은 URL 안전 id, 다른 경로는 다른 id', () => {
  const a = mediaId('AUDIO/업로드/노래.mp3');
  assert.equal(a, mediaId('AUDIO/업로드/노래.mp3'));
  assert.notEqual(a, mediaId('AUDIO/업로드/다른.mp3'));
  assert.match(a, /^[A-Za-z0-9_-]{22}$/);
});

test('경로: 미디어 폴더 밖으로 나가는 경로를 막는다', () => {
  const root = '/tmp/ulmaru-root';
  assert.equal(resolveMediaPath(root, 'a/b.mp4'), '/tmp/ulmaru-root/a/b.mp4');
  assert.equal(resolveMediaPath(root, '../etc/passwd'), null);
  assert.equal(resolveMediaPath(root, '/etc/passwd'), null);
  assert.equal(resolveMediaPath(root, ''), null);
  assert.equal(folderOf('a/b/c.mp4'), 'a/b');
  assert.equal(folderOf('c.mp4'), '');
});

test('표시: 파일 크기를 읽기 쉬운 단위로 만든다', () => {
  assert.equal(formatBytes(0), '0 B');
  assert.equal(formatBytes(512), '512 B');
  assert.equal(formatBytes(1536), '1.5 KB');
  assert.equal(formatBytes(1024 ** 2 * 2.5), '2.5 MB');
  assert.equal(formatBytes(1024 ** 3 * 1.25), '1.25 GB');
  assert.equal(formatBytes(-1), '0 B');
});

test('탐색: 하위 폴더의 미디어만 모으고 숨김·node_modules는 건너뛴다', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'ulmaru-view-'));
  try {
    await mkdir(join(dir, 'sub'), { recursive: true });
    await mkdir(join(dir, 'node_modules'), { recursive: true });
    await mkdir(join(dir, '.hidden'), { recursive: true });
    await writeFile(join(dir, 'b.mp4'), Buffer.from('bbbb'));
    await writeFile(join(dir, 'a.mp3'), Buffer.from('aaaa'));
    await writeFile(join(dir, 'sub', 'c.png'), Buffer.from('cccc'));
    await writeFile(join(dir, 'note.txt'), 'x');
    await writeFile(join(dir, 'node_modules', 'x.mp4'), 'x');
    await writeFile(join(dir, '.hidden', 'y.mp4'), 'y');
    await utimes(join(dir, 'a.mp3'), new Date(1000), new Date(1000));
    await utimes(join(dir, 'b.mp4'), new Date(3000), new Date(3000));
    await utimes(join(dir, 'sub', 'c.png'), new Date(2000), new Date(2000));

    const index = await scanMedia(dir);
    assert.equal(index.total, 3);
    assert.deepEqual(index.counts, { video: 1, audio: 1, image: 1 });
    assert.equal(index.truncated, false);
    assert.deepEqual(index.items.map((item) => item.name), ['b.mp4', 'c.png', 'a.mp3']);
    const png = index.items.find((item) => item.name === 'c.png');
    assert.equal(png.folder, 'sub');
    assert.equal(png.kind, 'image');
    assert.equal(png.size, 4);
    assert.equal(png.ext, 'png');
    for (const item of index.items) assert.match(item.id, /^[A-Za-z0-9_-]{22}$/);

    const limited = await scanMedia(dir, { limit: 2 });
    assert.equal(limited.total, 2);
    assert.equal(limited.truncated, true);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test('탐색: 없는 폴더는 빈 목록이 된다', async () => {
  const index = await scanMedia('/tmp/ulmaru-view-does-not-exist-xyz');
  assert.equal(index.total, 0);
  assert.deepEqual(index.counts, { video: 0, audio: 0, image: 0 });
  assert.equal(index.truncated, false);
});

test('필터·페이지: 검색어와 종류로 거르고 나눠 준다', () => {
  const items = [
    { name: '봄.mp4', folder: '영상', kind: 'video' },
    { name: '여름.mp3', folder: '소리', kind: 'audio' },
    { name: '가을.png', folder: '영상', kind: 'image' },
  ];
  assert.equal(filterMedia(items, { q: '봄' }).length, 1);
  assert.equal(filterMedia(items, { q: '영상' }).length, 2);
  assert.equal(filterMedia(items, { kind: 'audio' }).length, 1);
  assert.equal(filterMedia(items, { q: '없음' }).length, 0);
  assert.equal(filterMedia(items, {}).length, 3);

  const page = pageMedia(items, { offset: 1, limit: 1 });
  assert.deepEqual(page.items.map((item) => item.name), ['여름.mp3']);
  assert.equal(page.total, 3);
  assert.equal(page.offset, 1);
  assert.equal(page.limit, 1);
  assert.equal(pageMedia(items, {}).limit, DEFAULT_LIMIT);
  assert.equal(pageMedia(items, { limit: 99999 }).limit, MAX_LIMIT);
  assert.equal(pageMedia(items, { offset: -5 }).offset, 0);
});
