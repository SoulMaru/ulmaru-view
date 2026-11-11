// 얼마루 뷰 — 로컬 미디어 목록과 재생기.
// 요구 동작
//   1. 마우스 휠로 볼륨을 5씩 올리고 내린다.
//   2. 전체화면에서 마우스를 상단으로 올리면 X가 나타나고, 누르면 전체화면이 끝난다.
//   3. Enter는 전체화면 토글, Esc는 전체화면 종료.

import { clampVolume, stepVolume, wheelVolume, VOLUME_STEP } from '/volume.mjs';

const VOLUME_KEY = 'ulmaru-view-volume';
const PAGE_SIZE = 200;

const dom = {
  totalChip: document.querySelector('#total-chip'),
  refresh: document.querySelector('#refresh'),
  search: document.querySelector('#search'),
  filters: document.querySelector('#filters'),
  countAll: document.querySelector('#count-all'),
  countVideo: document.querySelector('#count-video'),
  countAudio: document.querySelector('#count-audio'),
  countImage: document.querySelector('#count-image'),
  rootPath: document.querySelector('#root-path'),
  list: document.querySelector('#media-list'),
  loadMore: document.querySelector('#load-more'),
  stage: document.querySelector('#stage'),
  empty: document.querySelector('#empty'),
  video: document.querySelector('#video'),
  audio: document.querySelector('#audio'),
  image: document.querySelector('#image'),
  audioCard: document.querySelector('#audio-card'),
  audioName: document.querySelector('#audio-name'),
  nowPlaying: document.querySelector('#now-playing'),
  nowTitle: document.querySelector('#now-title'),
  nowMeta: document.querySelector('#now-meta'),
  controls: document.querySelector('#controls'),
  play: document.querySelector('#play'),
  seek: document.querySelector('#seek'),
  time: document.querySelector('#time'),
  duration: document.querySelector('#duration'),
  mute: document.querySelector('#mute'),
  volume: document.querySelector('#volume'),
  volumeValue: document.querySelector('#volume-value'),
  fullscreen: document.querySelector('#fullscreen'),
  fsBar: document.querySelector('#fs-bar'),
  fsTitle: document.querySelector('#fs-title'),
  fsExit: document.querySelector('#fs-exit'),
  toast: document.querySelector('#toast'),
};

function readVolume() {
  try {
    return clampVolume(localStorage.getItem(VOLUME_KEY) ?? 80);
  } catch {
    return 80;
  }
}

const state = {
  root: '',
  counts: { video: 0, audio: 0, image: 0 },
  total: 0,
  items: [],
  offset: 0,
  limit: PAGE_SIZE,
  kind: '',
  q: '',
  current: null,
  volume: readVolume(),
  muted: false,
  fullscreen: false,
  loading: false,
};

// ── 작은 도우미 ────────────────────────────────────────────
function toast(text) {
  dom.toast.textContent = text;
  dom.toast.classList.add('show');
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => dom.toast.classList.remove('show'), 1600);
}

function formatTime(seconds) {
  const value = Number(seconds);
  if (!Number.isFinite(value) || value < 0) return '0:00';
  const total = Math.floor(value);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  return h > 0
    ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
    : `${m}:${String(s).padStart(2, '0')}`;
}

function activeMedia() {
  if (state.current?.kind === 'video') return dom.video;
  if (state.current?.kind === 'audio') return dom.audio;
  return null;
}

function syncPlayButton() {
  const media = activeMedia();
  const playing = Boolean(media && !media.paused && !media.ended);
  dom.play.textContent = playing ? '❚❚' : '▶';
  dom.play.setAttribute('aria-label', playing ? '일시정지' : '재생');
  dom.audioCard.classList.toggle('paused', !playing);
}

function updateVolumeUi() {
  dom.volume.value = String(state.volume);
  dom.volumeValue.textContent = `${state.volume}%`;
  dom.mute.textContent = state.muted || state.volume === 0 ? '🔇' : '🔊';
  dom.mute.setAttribute('aria-label', state.muted ? '음소거 해제' : '음소거');
}

function setVolume(value, { persist = true } = {}) {
  const next = clampVolume(value);
  state.volume = next;
  if (next > 0) state.muted = false;
  const media = activeMedia();
  if (media) {
    media.volume = next / 100;
    media.muted = state.muted;
  }
  updateVolumeUi();
  if (persist) {
    try { localStorage.setItem(VOLUME_KEY, String(next)); } catch { /* 저장 실패는 무시 */ }
  }
  return next;
}

// ── 데이터 ────────────────────────────────────────────────
async function fetchJson(url) {
  const response = await fetch(url, { cache: 'no-store' });
  if (!response.ok) throw new Error(`${url} HTTP ${response.status}`);
  return response.json();
}

function renderCounts() {
  dom.countAll.textContent = String(state.counts.video + state.counts.audio + state.counts.image);
  dom.countVideo.textContent = String(state.counts.video);
  dom.countAudio.textContent = String(state.counts.audio);
  dom.countImage.textContent = String(state.counts.image);
}

function updateActive() {
  for (const button of dom.list.querySelectorAll('.media-item')) {
    button.classList.toggle('active', button.dataset.id === state.current?.id);
  }
}

function renderList() {
  dom.list.replaceChildren();
  if (!state.items.length) {
    const li = document.createElement('li');
    li.className = 'empty-row';
    li.textContent = state.loading ? '미디어를 읽는 중…' : '조건에 맞는 미디어가 없습니다.';
    dom.list.append(li);
    return;
  }
  const fragment = document.createDocumentFragment();
  for (const item of state.items) {
    const li = document.createElement('li');
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'media-item';
    button.dataset.id = item.id;
    button.dataset.kind = item.kind;
    button.title = item.rel;

    const thumb = document.createElement('span');
    thumb.className = 'thumb';
    thumb.textContent = item.kind === 'video' ? '▶' : item.kind === 'audio' ? '♪' : '▣';

    const body = document.createElement('span');
    body.className = 'body';
    const name = document.createElement('span');
    name.className = 'name';
    name.textContent = item.name;
    const meta = document.createElement('span');
    meta.className = 'meta';
    meta.textContent = `${item.folder || '루트'} · ${item.sizeText || ''}`;
    body.append(name, meta);
    button.append(thumb, body);

    button.addEventListener('click', (event) => {
      select(item);
      event.currentTarget.blur();
    });
    li.append(button);
    fragment.append(li);
  }
  dom.list.append(fragment);
  updateActive();
}

async function loadState() {
  const data = await fetchJson('/api/state');
  state.root = data.root;
  state.counts = data.counts;
  state.total = data.total;
  dom.rootPath.textContent = data.root;
  dom.rootPath.title = data.root;
  dom.totalChip.textContent = `미디어 ${data.total.toLocaleString('ko-KR')}개`;
  renderCounts();
}

async function loadMedia({ reset = false, refresh = false } = {}) {
  if (state.loading) return;
  state.loading = true;
  if (reset) {
    state.offset = 0;
    state.items = [];
    renderList();
  }
  const params = new URLSearchParams({
    q: state.q,
    kind: state.kind,
    offset: String(state.offset),
    limit: String(state.limit),
  });
  if (refresh) params.set('refresh', '1');
  try {
    const data = await fetchJson(`/api/media?${params.toString()}`);
    state.root = data.root;
    state.counts = data.counts;
    state.total = data.total;
    state.items = reset ? data.items : state.items.concat(data.items);
    state.offset = data.offset + data.items.length;
    dom.rootPath.textContent = data.root;
    dom.rootPath.title = data.root;
    dom.totalChip.textContent = `미디어 ${state.total.toLocaleString('ko-KR')}개`;
    renderCounts();
    renderList();
    dom.loadMore.hidden = state.offset >= state.total;
    if (reset && (!state.current || !state.items.some((item) => item.id === state.current.id))) {
      if (state.items.length) select(state.items[0], { autoplay: false });
      else clearStage();
    }
  } catch (error) {
    console.error('미디어 목록 실패', error);
    state.items = [];
    renderList();
    toast('미디어 목록을 읽지 못했습니다.');
  } finally {
    state.loading = false;
    if (!state.items.length) renderList();
  }
}

// ── 재생 ──────────────────────────────────────────────────
function clearStage() {
  state.current = null;
  for (const media of [dom.video, dom.audio]) {
    media.pause();
    media.removeAttribute('src');
    media.load();
  }
  dom.image.removeAttribute('src');
  dom.video.hidden = true;
  dom.audio.hidden = true;
  dom.image.hidden = true;
  dom.audioCard.hidden = true;
  dom.controls.hidden = true;
  dom.nowPlaying.hidden = true;
  dom.empty.hidden = false;
  updateActive();
}

function select(item, { autoplay = true } = {}) {
  if (!item) return;
  const previous = activeMedia();
  if (previous) previous.pause();

  state.current = item;
  dom.empty.hidden = true;
  dom.nowPlaying.hidden = false;
  dom.nowTitle.textContent = item.name;
  dom.nowMeta.textContent = `${item.kind === 'video' ? '영상' : item.kind === 'audio' ? '오디오' : '이미지'} · ${item.folder || '루트'}${item.sizeText ? ` · ${item.sizeText}` : ''}`;

  dom.video.hidden = item.kind !== 'video';
  dom.audio.hidden = item.kind !== 'audio';
  dom.image.hidden = item.kind !== 'image';
  dom.audioCard.hidden = item.kind !== 'audio';
  dom.controls.hidden = item.kind === 'image';

  const src = `/api/stream/${item.id}`;
  if (item.kind === 'image') {
    dom.image.src = src;
    dom.image.alt = item.name;
  } else {
    const media = item.kind === 'video' ? dom.video : dom.audio;
    media.src = src;
    media.volume = state.volume / 100;
    media.muted = state.muted;
    media.load();
    if (item.kind === 'audio') dom.audioName.textContent = item.name;
    if (autoplay) {
      media.play().catch(() => { /* 자동 재생 차단은 사용자가 다시 누르면 된다 */ });
    }
  }

  dom.seek.value = '0';
  dom.time.textContent = '0:00';
  dom.duration.textContent = '0:00';
  dom.seek.max = '1000';
  syncPlayButton();
  updateActive();
  if (state.fullscreen) dom.fsTitle.textContent = item.name;
}

function playPause() {
  const media = activeMedia();
  if (!media) return;
  if (media.paused) media.play().catch(() => toast('재생할 수 없습니다.'));
  else media.pause();
}

// ── 전체화면 ──────────────────────────────────────────────
function isFullscreen() {
  return Boolean(document.fullscreenElement);
}

async function toggleFullscreen() {
  try {
    if (isFullscreen()) await document.exitFullscreen();
    else await dom.stage.requestFullscreen();
  } catch {
    toast('이 브라우저에서는 전체화면을 쓸 수 없습니다.');
  }
}

function revealExit() {
  if (!state.fullscreen) return;
  dom.fsBar.classList.add('show');
  dom.fsBar.setAttribute('aria-hidden', 'false');
  clearTimeout(revealExit.timer);
  revealExit.timer = setTimeout(hideExit, 2400);
}

function hideExit() {
  clearTimeout(revealExit.timer);
  dom.fsBar.classList.remove('show');
  dom.fsBar.setAttribute('aria-hidden', 'true');
}

document.addEventListener('fullscreenchange', () => {
  state.fullscreen = isFullscreen();
  dom.stage.classList.toggle('is-fullscreen', state.fullscreen);
  dom.fullscreen.setAttribute('aria-pressed', String(state.fullscreen));
  dom.fullscreen.textContent = state.fullscreen ? '⤢' : '⛶';
  if (state.fullscreen) {
    dom.fsTitle.textContent = state.current?.name || '전체화면';
    revealExit();
  } else {
    hideExit();
  }
});

// ── 입력 ──────────────────────────────────────────────────
function isTyping(target) {
  if (!target) return false;
  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
}

function isControl(target) {
  return Boolean(target?.closest?.('button, a'));
}

document.addEventListener('keydown', (event) => {
  if (event.metaKey || event.ctrlKey || event.altKey) return;
  if (isTyping(event.target)) return;

  if (event.key === 'Enter') {
    // 목록·도구 버튼에 키보드 포커스가 있을 때는 그 버튼의 기본 동작을 살린다.
    if (isControl(event.target) && !event.target.closest('#stage')) return;
    event.preventDefault();
    toggleFullscreen();
    return;
  }

  if (event.key === 'Escape') {
    if (isFullscreen()) {
      event.preventDefault();
      document.exitFullscreen().catch(() => {});
    }
    return;
  }

  if (event.code === 'Space') {
    if (isControl(event.target)) return;
    event.preventDefault();
    playPause();
    return;
  }

  if (!state.current) return;
  if (event.key === 'ArrowUp') {
    event.preventDefault();
    setVolume(state.volume + VOLUME_STEP);
    toast(`볼륨 ${state.volume}%`);
  } else if (event.key === 'ArrowDown') {
    event.preventDefault();
    setVolume(state.volume - VOLUME_STEP);
    toast(`볼륨 ${state.volume}%`);
  } else if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
    const media = activeMedia();
    if (!media) return;
    event.preventDefault();
    const delta = event.key === 'ArrowRight' ? 5 : -5;
    media.currentTime = Math.max(0, Math.min(media.duration || 0, media.currentTime + delta));
  }
});

// 휠: 볼륨 ±5. 이미지에는 적용하지 않는다.
dom.stage.addEventListener('wheel', (event) => {
  if (!state.current || state.current.kind === 'image') return;
  event.preventDefault();
  setVolume(wheelVolume(state.volume, event.deltaY));
  toast(`볼륨 ${state.volume}%`);
}, { passive: false });

// 전체화면에서 마우스가 상단으로 오면 X를 보여 준다.
dom.stage.addEventListener('mousemove', (event) => {
  if (state.fullscreen && event.clientY <= 96) revealExit();
});
dom.fsBar.addEventListener('mouseenter', () => {
  clearTimeout(revealExit.timer);
  dom.fsBar.classList.add('show');
  dom.fsBar.setAttribute('aria-hidden', 'false');
});
dom.fsBar.addEventListener('mouseleave', () => {
  revealExit.timer = setTimeout(hideExit, 900);
});
dom.fsExit.addEventListener('click', () => {
  document.exitFullscreen?.().catch(() => {});
});

// 무대 클릭: 재생/일시정지. 컨트롤 클릭은 제외.
dom.stage.addEventListener('click', (event) => {
  if (event.target.closest('.controls, .fs-bar, button, input')) return;
  if (!state.current) return;
  dom.stage.focus({ preventScroll: true });
  if (state.current.kind !== 'image') playPause();
});
dom.controls.addEventListener('click', (event) => event.stopPropagation());

// ── 컨트롤 연결 ───────────────────────────────────────────
dom.play.addEventListener('click', (event) => {
  playPause();
  event.currentTarget.blur();
});
dom.mute.addEventListener('click', (event) => {
  state.muted = !state.muted;
  const media = activeMedia();
  if (media) media.muted = state.muted;
  updateVolumeUi();
  event.currentTarget.blur();
});
dom.volume.addEventListener('input', () => {
  setVolume(Number(dom.volume.value));
});
dom.fullscreen.addEventListener('click', (event) => {
  toggleFullscreen();
  event.currentTarget.blur();
});
dom.seek.addEventListener('input', () => {
  const media = activeMedia();
  if (!media || !Number.isFinite(media.duration) || media.duration <= 0) return;
  media.currentTime = (Number(dom.seek.value) / 1000) * media.duration;
});

for (const media of [dom.video, dom.audio]) {
  media.addEventListener('play', syncPlayButton);
  media.addEventListener('pause', syncPlayButton);
  media.addEventListener('ended', syncPlayButton);
  media.addEventListener('timeupdate', () => {
    if (media !== activeMedia()) return;
    const duration = Number(media.duration);
    if (Number.isFinite(duration) && duration > 0) {
      dom.seek.value = String(Math.round((media.currentTime / duration) * 1000));
      dom.duration.textContent = formatTime(duration);
    }
    dom.time.textContent = formatTime(media.currentTime);
  });
  media.addEventListener('loadedmetadata', () => {
    if (media !== activeMedia()) return;
    dom.duration.textContent = formatTime(media.duration);
  });
  media.addEventListener('error', () => {
    if (media !== activeMedia()) return;
    toast('이 파일을 재생할 수 없습니다.');
  });
}

// ── 목록 도구 ─────────────────────────────────────────────
let searchTimer;
dom.search.addEventListener('input', () => {
  state.q = dom.search.value.trim();
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => loadMedia({ reset: true }), 250);
});

dom.filters.addEventListener('click', (event) => {
  const button = event.target.closest('button[data-kind]');
  if (!button) return;
  state.kind = button.dataset.kind || '';
  for (const chip of dom.filters.querySelectorAll('button')) {
    chip.setAttribute('aria-pressed', String(chip === button));
  }
  button.blur();
  loadMedia({ reset: true });
});

dom.loadMore.addEventListener('click', (event) => {
  event.currentTarget.blur();
  loadMedia();
});

dom.refresh.addEventListener('click', async (event) => {
  event.currentTarget.blur();
  try {
    await loadState();
    await loadMedia({ reset: true, refresh: true });
    toast('미디어 목록을 새로 읽었습니다.');
  } catch {
    toast('새로고침에 실패했습니다.');
  }
});

// ── 시작 ──────────────────────────────────────────────────
setVolume(state.volume, { persist: false });
updateVolumeUi();
loadState()
  .then(() => loadMedia({ reset: true }))
  .catch((error) => {
    console.error('초기화 실패', error);
    dom.rootPath.textContent = '미디어 폴더를 읽지 못했습니다.';
    renderList();
  });

// 검증·자동화용 최소 인터페이스(화면 상태를 바꾸는 동작은 명시적으로만).
window.__ULMARU_VIEW__ = {
  state: () => ({
    root: state.root,
    total: state.total,
    loaded: state.items.length,
    counts: { ...state.counts },
    current: state.current ? { id: state.current.id, name: state.current.name, kind: state.current.kind } : null,
    volume: state.volume,
    muted: state.muted,
    fullscreen: isFullscreen(),
    exitVisible: dom.fsBar.classList.contains('show'),
    step: VOLUME_STEP,
  }),
  items: () => state.items.map((item) => ({ id: item.id, name: item.name, kind: item.kind })),
  select: (id) => {
    const item = state.items.find((entry) => entry.id === id);
    if (!item) return false;
    select(item, { autoplay: false });
    return true;
  },
  volume: () => state.volume,
  setVolume: (value) => setVolume(value),
  stepVolume: (direction) => {
    setVolume(stepVolume(state.volume, direction));
    return state.volume;
  },
  wheel: (deltaY) => {
    setVolume(wheelVolume(state.volume, deltaY));
    return state.volume;
  },
  toggleFullscreen,
  isFullscreen,
  showExit: revealExit,
  exitVisible: () => dom.fsBar.classList.contains('show'),
  refresh: async () => {
    await loadState();
    await loadMedia({ reset: true, refresh: true });
    return true;
  },
};
