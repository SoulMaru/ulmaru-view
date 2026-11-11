// 얼마루 미디어 인덱스 — 파일 시스템 탐색과 볼륨 계산의 순수 로직.
// 브라우저·HTTP에 의존하지 않으므로 node --test 로 그대로 시험한다.

import { readdir, stat } from 'node:fs/promises';
import { extname, isAbsolute, join, relative, resolve } from 'node:path';
import { createHash } from 'node:crypto';

export { VOLUME_STEP, clampVolume, stepVolume, wheelVolume } from '../public/volume.mjs';

export const VIDEO_EXT = new Set(['.mp4', '.webm', '.mov', '.mkv', '.m4v', '.avi', '.ogv']);
export const AUDIO_EXT = new Set(['.mp3', '.wav', '.m4a', '.ogg', '.flac', '.aac', '.opus']);
export const IMAGE_EXT = new Set(['.png', '.jpg', '.jpeg', '.webp', '.gif', '.avif', '.bmp']);

export const MEDIA_KINDS = Object.freeze({
  video: [...VIDEO_EXT],
  audio: [...AUDIO_EXT],
  image: [...IMAGE_EXT],
});

// 탐색에서 건너뛸 폴더. 저장소·휴지통·시스템 폴더는 미디어가 아니다.
export const SKIP_DIRS = new Set([
  'node_modules', '.git', '.cache', '.tmp.drivedownload', '.tmp.driveupload',
  '$RECYCLE.BIN', 'System Volume Information', 'Recovery',
]);

export const DEFAULT_LIMIT = 200;
export const MAX_LIMIT = 1000;
export const MAX_FILES = 20000;

export function kindOf(pathOrExt) {
  const ext = String(pathOrExt || '').toLowerCase().startsWith('.')
    ? String(pathOrExt).toLowerCase()
    : extname(String(pathOrExt || '')).toLowerCase();
  if (VIDEO_EXT.has(ext)) return 'video';
  if (AUDIO_EXT.has(ext)) return 'audio';
  if (IMAGE_EXT.has(ext)) return 'image';
  return null;
}

export function mediaId(relPath) {
  return createHash('sha1').update(String(relPath)).digest('base64url').slice(0, 22);
}

// id는 경로의 해시라 재시작해도 같다. 스트리밍은 인덱스에서 경로를 찾는다.
export function resolveMediaPath(root, relPath) {
  const base = resolve(String(root || '.'));
  const abs = resolve(base, String(relPath || ''));
  const rel = relative(base, abs);
  if (!rel || rel.startsWith('..') || isAbsolute(rel)) return null;
  return abs;
}

export function formatBytes(bytes) {
  const value = Number(bytes);
  if (!Number.isFinite(value) || value < 0) return '0 B';
  if (value < 1024) return `${value} B`;
  if (value < 1024 ** 2) return `${(value / 1024).toFixed(1)} KB`;
  if (value < 1024 ** 3) return `${(value / 1024 ** 2).toFixed(1)} MB`;
  return `${(value / 1024 ** 3).toFixed(2)} GB`;
}

export function folderOf(relPath) {
  const parts = String(relPath || '').split('/');
  parts.pop();
  return parts.join('/');
}

// root 아래의 미디어 파일을 재귀 탐색한다. 심볼릭 링크는 따라가지 않는다.
export async function scanMedia(root, { limit = MAX_FILES } = {}) {
  const base = resolve(String(root || '.'));
  const items = [];
  let visited = 0;
  let truncated = false;
  const stack = [''];
  while (stack.length) {
    const relDir = stack.pop();
    let entries;
    try {
      entries = await readdir(join(base, relDir), { withFileTypes: true });
    } catch {
      continue;
    }
    for (const entry of entries) {
      if (entry.isSymbolicLink()) continue;
      const rel = relDir ? `${relDir}/${entry.name}` : entry.name;
      if (entry.isDirectory()) {
        if (SKIP_DIRS.has(entry.name) || entry.name.startsWith('.')) continue;
        stack.push(rel);
        continue;
      }
      if (!entry.isFile()) continue;
      const kind = kindOf(entry.name);
      if (!kind) continue;
      visited += 1;
      if (visited > limit) { truncated = true; break; }
      let info;
      try {
        info = await stat(join(base, rel));
      } catch {
        continue;
      }
      items.push({
        id: mediaId(rel),
        rel,
        name: entry.name,
        folder: folderOf(rel),
        ext: extname(entry.name).slice(1).toLowerCase(),
        kind,
        size: info.size,
        mtime: Math.round(info.mtimeMs),
      });
    }
    if (truncated) break;
  }
  items.sort((a, b) => b.mtime - a.mtime || a.rel.localeCompare(b.rel, 'ko'));
  const counts = { video: 0, audio: 0, image: 0 };
  for (const item of items) counts[item.kind] += 1;
  return {
    root: base,
    items,
    total: items.length,
    counts,
    truncated,
    scannedAt: Date.now(),
  };
}

export function filterMedia(items, { q = '', kind = '' } = {}) {
  const needle = String(q || '').trim().toLowerCase();
  const wanted = String(kind || '').trim().toLowerCase();
  return (Array.isArray(items) ? items : []).filter((item) => {
    if (wanted && item.kind !== wanted) return false;
    if (!needle) return true;
    return `${item.name} ${item.folder}`.toLowerCase().includes(needle);
  });
}

export function pageMedia(items, { offset = 0, limit = DEFAULT_LIMIT } = {}) {
  const start = Math.max(0, Math.floor(Number(offset) || 0));
  const size = Math.max(1, Math.min(MAX_LIMIT, Math.floor(Number(limit) || DEFAULT_LIMIT)));
  const list = Array.isArray(items) ? items : [];
  return { items: list.slice(start, start + size), total: list.length, offset: start, limit: size };
}
