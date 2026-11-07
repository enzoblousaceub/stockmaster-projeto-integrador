import { Component, inject } from '@angular/core';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ThemeMode, ThemeService } from '../../theme.service';

@Component({
  selector: 'app-settings-dialog',
  standalone: true,
  imports: [MatDialogModule, MatIconModule],
  template: `
    <div class="dlg-head">
      <h2 mat-dialog-title id="settings-title">Configurações</h2>
      <button type="button" class="close-btn" mat-dialog-close aria-label="Fechar configurações">
        <mat-icon aria-hidden="true">close</mat-icon>
      </button>
    </div>

    <mat-dialog-content>
      <!-- Cada opção nova entra como uma <section class="setting"> adicional abaixo -->
      <section class="setting" aria-labelledby="sec-appearance">
        <h3 id="sec-appearance">Aparência</h3>
        <p class="hint" id="appearance-hint">Escolha o tema das cores do sistema.</p>
        <div class="segmented" role="radiogroup" aria-labelledby="sec-appearance" aria-describedby="appearance-hint">
          @for (o of themes; track o.value) {
            <label class="seg" [class.on]="theme.mode() === o.value">
              <input type="radio" name="theme" [value]="o.value" [checked]="theme.mode() === o.value"
                (change)="theme.set(o.value)">
              <mat-icon aria-hidden="true">{{ o.icon }}</mat-icon>
              <span>{{ o.label }}</span>
            </label>
          }
        </div>
      </section>
    </mat-dialog-content>
  `,
  styles: [`
    :host { display: block; color: var(--sm-text); }
    .dlg-head { display: flex; align-items: center; justify-content: space-between; padding-right: 12px; }
    h2 { margin: 0; flex: 1; }
    .close-btn {
      display: inline-flex; align-items: center; justify-content: center;
      width: 48px; height: 48px; border: 0; border-radius: var(--sm-radius);
      background: transparent; color: var(--sm-text-2); cursor: pointer;
    }
    .close-btn:hover { background: var(--sm-surface-2); color: var(--sm-text); }
    .setting + .setting { margin-top: 24px; padding-top: 24px; border-top: 1px solid var(--sm-border); }
    h3 { margin: 0 0 4px; font-size: 1rem; font-weight: 600; }
    .hint { margin: 0 0 12px; color: var(--sm-text-2); font-size: 0.875rem; }
    .segmented {
      display: grid; grid-template-columns: 1fr 1fr; gap: 4px; padding: 4px;
      border-radius: var(--sm-radius); background: var(--sm-surface-2); border: 1px solid var(--sm-border);
    }
    .seg {
      position: relative; display: flex; align-items: center; justify-content: center; gap: 8px;
      min-height: 48px; border-radius: 6px; cursor: pointer; font-weight: 500; color: var(--sm-text-2);
      transition: background-color .2s, color .2s;
    }
    .seg input { position: absolute; inset: 0; opacity: 0; margin: 0; cursor: pointer; }
    .seg:hover { color: var(--sm-text); }
    .seg.on { background: var(--sm-surface); color: var(--sm-primary); font-weight: 600; box-shadow: 0 0 0 1px var(--sm-border-strong); }
    .seg:has(input:focus-visible) { outline: 2px solid var(--sm-primary); outline-offset: 2px; }
  `]
})
export class SettingsDialogComponent {
  readonly theme = inject(ThemeService);
  readonly themes: { value: ThemeMode; label: string; icon: string }[] = [
    { value: 'light', label: 'Claro', icon: 'light_mode' },
    { value: 'dark', label: 'Escuro', icon: 'dark_mode' }
  ];
}
