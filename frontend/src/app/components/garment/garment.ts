import { Component, computed, input } from '@angular/core';

type Kind = 'pants' | 'shorts' | 'tshirt' | 'shirt' | 'dress' | 'hoodie' | 'jacket' | 'sock';

/** Silhuetas simples (viewBox 64x64), no lugar da foto enquanto o dono não fotografa a peça. */
const PATHS: Record<Kind, string> = {
  pants: 'M20 7h24l4 50H36.5L32 26l-4.5 31H16z',
  shorts: 'M17 14h30l4 27H38.5L32 29l-6.5 12H13z',
  tshirt: 'M23 9 10 15.5l5 12 6-2.8V56h22V24.7l6 2.8 5-12L41 9c-1.5 4.3-4.8 6.8-9 6.8S24.5 13.3 23 9z',
  shirt: 'M24 8 11 13.5 6 48l7.5 1L18 26v30h28V26l4.5 23 7.5-1-5-34.5L40 8l-8 9z',
  dress: 'M26 6h12l-1.5 11L47 57H17l10.5-40z',
  hoodie: 'M23 12a9 9 0 0 1 18 0l12 5.5-3.5 29-7.5-1V57H22V45.5l-7.5 1-3.5-29z',
  jacket: 'M25 8 11 14 7 50l8 1 3-22v27h12V18l2-4 2 4v38h12V29l3 22 8-1-4-36-14-6-7 6z',
  sock: 'M24 6h15v29L27 53c-3.5 5-12 3-11.5-3.5.2-2 1.2-3.8 2.5-5l6-9z'
};

function kindOf(text: string): Kind {
  const t = text.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
  if (/bermuda|short/.test(t)) return 'shorts';
  if (/calca|legging/.test(t)) return 'pants';
  if (/vestido|saia/.test(t)) return 'dress';
  if (/moletom|blusao|hoodie/.test(t)) return 'hoodie';
  if (/jaqueta|casaco|blazer|colete/.test(t)) return 'jacket';
  if (/meia|acessorio|cinto|bone/.test(t)) return 'sock';
  if (/camisa(?!ta)|social/.test(t)) return 'shirt';
  return 'tshirt';
}

@Component({
  selector: 'app-garment',
  standalone: true,
  template: `<svg viewBox="0 0 64 64" aria-hidden="true" focusable="false"><path [attr.d]="path()" /></svg>`,
  styles: [`
    :host { display: flex; align-items: center; justify-content: center; width: 100%; height: 100%; color: var(--sm-primary); }
    svg { width: 62%; height: 62%; max-width: 120px; }
    path { fill: currentColor; fill-opacity: 0.16; stroke: currentColor; stroke-width: 2.4; stroke-linejoin: round; }
  `]
})
export class GarmentComponent {
  /** texto usado para reconhecer a peça (tipo + nome) */
  readonly of = input('');
  readonly path = computed(() => PATHS[kindOf(this.of())]);
}
