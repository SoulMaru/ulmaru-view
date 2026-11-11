// 얼마루 뷰 — 로컬 미디어를 탐색해 브라우저에서 재생하는 작은 서버.
// 외부 의존성 없이 Node 22 표준 라이브러리만 쓴다. 기본은 127.0.0.1 바인딩(로컬 전용).
//
// 환경변수
//   PORT                기본 8793
//   HOST                기본 127.0.0.1
//   ULMARU_MEDIA_ROOT   미디어 폴더(없으면 아래 후보에서 처음 존재하는 폴더)

import { createServer } from 'node:http';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, rename, stat, unlink, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { extname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { homedir } from 'node:os';

import {
  scanMedia, filterMedia, pageMedia, resolveMediaPath,
  formatBytes, DEFAULT_LIMIT, MAX_LIMIT, VOLUME_STEP,
} from './src/media-index.mjs';

const HERE = fileURLToPath(new URL('.', import.meta.url));
const PUBLIC_DIR = join(HERE, 'public');
const DEFAULT_PORT = 8793;
const DEFAULT_HOST = '127.0.0.1';
const INDEX_TTL = 60_000;
// 색인 캐시는 미디어 폴더가 아니라 사용자 캐시 폴더에 둔다(재시작해도 바로 뜬다).
const CACHE_DIR = process.env.ULMARU_CACHE_DIR || join(homedir(), '.cache', 'ulmaru-view');
const CACHE_VERSION = 1;

const STATIC = new Map([
  ['/', ['index.html', 'text/html; charset=utf-8']],
  ['/index.html', ['index.html', 'text/html; charset=utf-8']],
  ['/app.js', ['app.js', 'text/javascript; charset=utf-8']],
  ['/volume.mjs', ['volume.mjs', 'text/javascript; charset=utf-8']],
  ['/styles.css', ['styles.css', 'text/css; charset=utf-8']],
  ['/favicon.svg', ['favicon.svg', 'image/svg+xml']],
]);

const MIME = {
  mp4: 'video/mp4', m4v: 'video/mp4', webm: 'video/webm', ogv: 'video/ogg',
  mov: 'video/quicktime', mkv: 'video/x-matroska', avi: 'video/x-msvideo',
  mp3: 'audio/mpeg', wav: 'audio/wav', m4a: 'audio/mp4', ogg: 'audio/ogg',
  flac: 'audio/flac', aac: 'audio/aac', opus: 'audio/ogg',
  png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', webp: 'image/webp',
  gif: 'image/gif', avif: 'image/avif', bmp: 'image/bmp',
};

export function defaultRoots() {
  return [
    process.env.ULMARU_MEDIA_ROOT,
    '/mnt/c/AI_RUNTIME_PC1/04_CONTENT_STUDIO',
    join(homedir(), 'soulmaru-2', 'downloads'),
    join(process.cwd(), 'media'),
  ].filter(Boolean);
}

export async function pickRoot() {
  for (const candidate of defaultRoots()) {
    try {
      const info = await stat(candidate);
      if (info.isDirectory()) return resolve(candidate);
    } catch { /* 다음 후보 */ }
  }
  return resolve(process.cwd());
}

function sendJson(res, status, value) {
  const body = JSON.stringify(value);
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body),
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  });
  res.end(body);
}

function sendText(res, status, text) {
  res.writeHead(status, {
    'Content-Type': 'text/plain; charset=utf-8',
    'Content-Length': Buffer.byteLength(text),
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  });
  res.end(text);
}

function parseRange(header, size) {
  const match = /^bytes=(\d*)-(\d*)$/.exec(String(header || '').trim());
  if (!match) return null;
  let start;
  let end;
  if (match[1] === '') {
    const suffix = Number(match[2]);
    if (!Number.isFinite(suffix) || suffix <= 0) return null;
    start = Math.max(0, size - suffix);
    end = size - 1;
  } else {
    start = Number(match[1]);
    end = match[2] === '' ? size - 1 : Number(match[2]);
    if (end >= size) end = size - 1;
  }
  if (!Number.isFinite(start) || !Number.isFinite(end) || start > end || start >= size) return null;
  return { start, end };
}

export function createApp({
  root = process.cwd(),
  publicDir = PUBLIC_DIR,
  ttl = INDEX_TTL,
  cacheDir = CACHE_DIR,
} = {}) {
  let cache = null;
  let cacheAt = 0;
  let pending = null;
  let diskChecked = false;

  const cacheFile = cacheDir
    ? join(cacheDir, `${createHash('sha1').update(resolve(root)).digest('hex').slice(0, 16)}.json`)
    : null;

  async function readDiskCache() {
    if (!cacheFile) return null;
    try {
      const data = JSON.parse(await readFile(cacheFile, 'utf8'));
      if (data?.version !== CACHE_VERSION || data.root !== resolve(root) || !Array.isArray(data.items)) return null;
      const counts = { video: 0, audio: 0, image: 0 };
      for (const item of data.items) if (counts[item.kind] !== undefined) counts[item.kind] += 1;
      return {
        root: data.root,
        items: data.items,
        total: data.items.length,
        counts,
        truncated: Boolean(data.truncated),
        scannedAt: Number(data.scannedAt) || 0,
      };
    } catch {
      return null;
    }
  }

  async function writeDiskCache(index) {
    if (!cacheFile) return;
    const body = JSON.stringify({
      version: CACHE_VERSION,
      root: index.root,
      scannedAt: index.scannedAt,
      truncated: index.truncated,
      items: index.items,
    });
    const temp = `${cacheFile}.${process.pid}.tmp`;
    try {
      await mkdir(cacheDir, { recursive: true });
      await writeFile(temp, body, 'utf8');
      await rename(temp, cacheFile);
    } catch {
      await unlink(temp).catch(() => {});
    }
  }

  function startScan() {
    if (pending) return pending;
    pending = scanMedia(root)
      .then(async (index) => {
        cache = index;
        cacheAt = Date.now();
        await writeDiskCache(index);
        return index;
      })
      .finally(() => { pending = null; });
    return pending;
  }

  // 캐시가 있으면 낡았더라도 바로 돌려주고, 뒤에서 새로 읽는다(느린 드라이브 대비).
  async function getIndex(refresh = false) {
    if (!diskChecked) {
      diskChecked = true;
      const disk = await readDiskCache();
      if (disk) { cache = disk; cacheAt = disk.scannedAt; }
    }
    const now = Date.now();
    if (refresh) return startScan();
    if (!cache) return startScan();
    if (now - cacheAt >= ttl) startScan().catch(() => {});
    return cache;
  }

  async function serveStatic(req, res, pathname) {
    const entry = STATIC.get(pathname);
    if (!entry) return false;
    const [file, type] = entry;
    try {
      const body = await readFile(join(publicDir, file));
      res.writeHead(200, {
        'Content-Type': type,
        'Content-Length': body.length,
        'Cache-Control': 'no-cache',
        'X-Content-Type-Options': 'nosniff',
        'Referrer-Policy': 'no-referrer',
      });
      if (req.method === 'HEAD') res.end();
      else res.end(body);
      return true;
    } catch {
      sendText(res, 404, '찾을 수 없습니다.');
      return true;
    }
  }

  async function serveStream(req, res, id) {
    let index = await getIndex(false);
    let item = index.items.find((entry) => entry.id === id);
    if (!item) {
      index = await getIndex(true);
      item = index.items.find((entry) => entry.id === id);
    }
    if (!item) return sendText(res, 404, '미디어를 찾을 수 없습니다.');

    const abs = resolveMediaPath(root, item.rel);
    if (!abs) return sendText(res, 403, '허용되지 않은 경로입니다.');

    let info;
    try {
      info = await stat(abs);
      if (!info.isFile()) throw new Error('not a file');
    } catch {
      return sendText(res, 404, '파일이 없습니다.');
    }

    const type = MIME[item.ext] || 'application/octet-stream';
    const range = parseRange(req.headers.range, info.size);
    const common = {
      'Content-Type': type,
      'Accept-Ranges': 'bytes',
      'Cache-Control': 'private, max-age=60',
      'X-Content-Type-Options': 'nosniff',
    };

    if (req.headers.range && !range) {
      res.writeHead(416, { ...common, 'Content-Range': `bytes */${info.size}` });
      return res.end();
    }

    if (!range) {
      res.writeHead(200, { ...common, 'Content-Length': info.size });
      if (req.method === 'HEAD') return res.end();
      return createReadStream(abs).on('error', () => res.destroy()).pipe(res);
    }

    res.writeHead(206, {
      ...common,
      'Content-Range': `bytes ${range.start}-${range.end}/${info.size}`,
      'Content-Length': range.end - range.start + 1,
    });
    if (req.method === 'HEAD') return res.end();
    return createReadStream(abs, { start: range.start, end: range.end })
      .on('error', () => res.destroy())
      .pipe(res);
  }

  const server = createServer(async (req, res) => {
    try {
      if (req.method !== 'GET' && req.method !== 'HEAD') {
        res.setHeader('Allow', 'GET, HEAD');
        return sendText(res, 405, 'GET 또는 HEAD만 지원합니다.');
      }
      const url = new URL(req.url || '/', 'http://127.0.0.1');
      const { pathname } = url;

      if (pathname === '/api/state') {
        const index = await getIndex(url.searchParams.get('refresh') === '1');
        return sendJson(res, 200, {
          root: index.root,
          total: index.total,
          counts: index.counts,
          truncated: index.truncated,
          scannedAt: index.scannedAt,
          volumeStep: VOLUME_STEP,
          limit: DEFAULT_LIMIT,
          maxLimit: MAX_LIMIT,
        });
      }

      if (pathname === '/api/media') {
        const index = await getIndex(url.searchParams.get('refresh') === '1');
        const filtered = filterMedia(index.items, {
          q: url.searchParams.get('q') || '',
          kind: url.searchParams.get('kind') || '',
        });
        const page = pageMedia(filtered, {
          offset: url.searchParams.get('offset'),
          limit: url.searchParams.get('limit'),
        });
        return sendJson(res, 200, {
          root: index.root,
          total: page.total,
          offset: page.offset,
          limit: page.limit,
          counts: index.counts,
          scannedAt: index.scannedAt,
          items: page.items.map((item) => ({ ...item, sizeText: formatBytes(item.size) })),
        });
      }

      const streamMatch = /^\/api\/stream\/([A-Za-z0-9_-]{8,64})$/.exec(pathname);
      if (streamMatch) return serveStream(req, res, streamMatch[1]);

      if (pathname === '/api/health') return sendJson(res, 200, { ok: true, root, uptime: process.uptime() });

      if (await serveStatic(req, res, pathname)) return;
      sendText(res, 404, '찾을 수 없습니다.');
    } catch (error) {
      console.error('요청 처리 실패', error);
      if (!res.headersSent) sendText(res, 500, '서버 오류');
      else res.destroy();
    }
  });

  server.on('close', () => { cache = null; pending = null; });
  return server;
}

export async function startServer({ port = DEFAULT_PORT, host = DEFAULT_HOST, root } = {}) {
  const mediaRoot = root || await pickRoot();
  const server = createApp({ root: mediaRoot });
  await new Promise((resolveListen, reject) => {
    server.once('error', reject);
    server.listen(port, host, () => resolveListen());
  });
  const address = server.address();
  return {
    server,
    root: mediaRoot,
    host,
    port: typeof address === 'object' && address ? address.port : port,
    url: `http://${host}:${typeof address === 'object' && address ? address.port : port}`,
  };
}

const isMain = process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url;
if (isMain) {
  const port = Number(process.env.PORT) || DEFAULT_PORT;
  const host = process.env.HOST || DEFAULT_HOST;
  startServer({ port, host })
    .then(({ url, root }) => {
      console.log(`얼마루 뷰: ${url}`);
      console.log(`미디어 폴더: ${root}`);
      if (host !== '127.0.0.1' && host !== 'localhost') {
        console.warn('경고: 로컬 전용이 아닌 주소에 바인딩했습니다.');
      }
    })
    .catch((error) => {
      console.error('서버를 시작하지 못했습니다.', error);
      process.exitCode = 1;
    });
}
