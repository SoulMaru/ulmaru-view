// 볼륨 계산 — 브라우저와 Node 시험이 함께 쓰는 순수 모듈.
// 볼륨은 0~100 정수이며, 마우스 휠·키보드·슬라이더 모두 5 단위로 움직인다.

export const VOLUME_STEP = 5;

export function clampVolume(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) return 0;
  return Math.max(0, Math.min(100, Math.round(number)));
}

export function stepVolume(current, direction) {
  const sign = Math.sign(Number(direction) || 0);
  if (!sign) return clampVolume(current);
  return clampVolume(clampVolume(current) + sign * VOLUME_STEP);
}

// 휠은 위로 굴리면(deltaY < 0) 볼륨을 올리고, 아래로 굴리면 내린다.
export function wheelVolume(current, deltaY) {
  const delta = Number(deltaY);
  if (!Number.isFinite(delta) || delta === 0) return clampVolume(current);
  return stepVolume(current, delta < 0 ? 1 : -1);
}
