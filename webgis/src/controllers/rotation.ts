import type { MapContext } from '../map/mapInit';

export function wireRotation(ctx: MapContext): void {
  const input = document.getElementById('rotation-input') as HTMLInputElement | null;
  const label = document.getElementById('rotation-value') as HTMLSpanElement | null;
  if (!input || !label) return;

  input.addEventListener('input', () => {
    const deg = Number(input.value);
    label.textContent = String(deg);
    ctx.view.setRotation((deg * Math.PI) / 180);
  });

  ctx.view.on('change:rotation', () => {
    const deg = Math.round((ctx.view.getRotation() * 180) / Math.PI);
    input.value = String(deg);
    label.textContent = String(deg);
  });
}
