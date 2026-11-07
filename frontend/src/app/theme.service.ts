import { Injectable, effect, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark' | 'system';
const KEY = 'sm-theme';
const ORDER: ThemeMode[] = ['light', 'dark', 'system'];

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly mode = signal<ThemeMode>(this.read());

  constructor() {
    effect(() => this.apply(this.mode()));
  }

  cycle(): void {
    const next = ORDER[(ORDER.indexOf(this.mode()) + 1) % ORDER.length];
    this.mode.set(next);
    try { localStorage.setItem(KEY, next); } catch { /* armazenamento indisponível */ }
  }

  private read(): ThemeMode {
    try {
      const v = localStorage.getItem(KEY);
      if (v === 'light' || v === 'dark' || v === 'system') return v;
    } catch { /* ignore */ }
    return 'light';
  }

  private apply(mode: ThemeMode): void {
    const root = document.documentElement;
    if (mode === 'system') root.removeAttribute('data-theme');
    else root.setAttribute('data-theme', mode);
    const dark = mode === 'dark' || (mode === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', dark ? '#0b1220' : '#f6f7f9');
  }
}
