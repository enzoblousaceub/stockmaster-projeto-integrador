import { Injectable, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark';
const KEY = 'sm-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly mode = signal<ThemeMode>(this.initial());

  constructor() {
    this.apply(this.mode());
  }

  /** Define o tema com transição suave (View Transitions com fallback em CSS). */
  set(mode: ThemeMode): void {
    if (mode === this.mode()) return;
    const run = () => { this.mode.set(mode); this.apply(mode); };
    const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
    const root = document.documentElement;
    const doc = document as Document & { startViewTransition?: (cb: () => void) => unknown };

    if (!reduced && typeof doc.startViewTransition === 'function') {
      doc.startViewTransition(run);
    } else if (!reduced) {
      root.classList.add('theme-fade');
      run();
      setTimeout(() => root.classList.remove('theme-fade'), 350);
    } else {
      run();
    }
    try { localStorage.setItem(KEY, mode); } catch { /* armazenamento indisponível */ }
  }

  /** Valor salvo; o antigo 'auto'/'system' vira claro/escuro (pelo SO) uma única vez. */
  private initial(): ThemeMode {
    let saved: string | null = null;
    try { saved = localStorage.getItem(KEY); } catch { /* ignore */ }
    if (saved === 'light' || saved === 'dark') return saved;
    if (saved === 'system' || saved === 'auto') {
      const m: ThemeMode = matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
      try { localStorage.setItem(KEY, m); } catch { /* ignore */ }
      return m;
    }
    return 'light';
  }

  private apply(mode: ThemeMode): void {
    document.documentElement.setAttribute('data-theme', mode);
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', mode === 'dark' ? '#0b1220' : '#f6f7f9');
  }
}
