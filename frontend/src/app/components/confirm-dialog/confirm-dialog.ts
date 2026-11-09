import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

export interface ConfirmData {
  title: string;
  message: string;
  confirmLabel: string;
  icon?: string;
  danger?: boolean;
}

/** Confirmação genérica. Foco inicial em "Cancelar" (ação segura); ESC cancela. */
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [MatDialogModule, MatIconModule],
  template: `
    <div class="head">
      <span class="icon" [class.danger]="data.danger" aria-hidden="true"><mat-icon>{{ data.icon || 'help' }}</mat-icon></span>
      <h2 mat-dialog-title id="confirm-title">{{ data.title }}</h2>
    </div>
    <mat-dialog-content><p id="confirm-desc">{{ data.message }}</p></mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" class="btn btn-outline" [mat-dialog-close]="false" cdkFocusInitial>Cancelar</button>
      <button type="button" class="btn" [class.btn-danger]="data.danger" [class.btn-primary]="!data.danger" [mat-dialog-close]="true" id="btn-confirm">
        {{ data.confirmLabel }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    :host { display: block; color: var(--sm-text); }
    .head { display: flex; align-items: center; gap: 12px; padding: 4px 24px 0; }
    .head h2 { margin: 0; padding: 0; flex: 1; font-size: 1.125rem; font-weight: 600; }
    .head h2::before { display: none; }
    .icon { display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px; border-radius: 50%; background: var(--sm-primary-soft); color: var(--sm-primary); }
    .icon.danger { background: var(--sm-danger-soft); color: var(--sm-danger); }
    p { margin: 8px 0 0; color: var(--sm-text-2); overflow-wrap: anywhere; }
    mat-dialog-actions { gap: 8px; padding: 12px 24px 20px; }
    .btn { display: inline-flex; align-items: center; justify-content: center; min-height: 48px; padding: 0 20px; border-radius: var(--sm-radius); font: inherit; font-weight: 600; cursor: pointer; }
    .btn-outline { border: 1px solid var(--sm-control-border); background: transparent; color: var(--sm-text); }
    .btn-outline:hover { background: var(--sm-surface-2); }
    .btn-primary { border: 1px solid var(--sm-primary); background: var(--sm-primary); color: var(--sm-on-primary); }
    .btn-danger { border: 1px solid var(--sm-danger); background: var(--sm-danger); color: var(--sm-on-danger); }
    @media (max-width: 480px) { mat-dialog-actions { flex-direction: column-reverse; align-items: stretch; } }
  `]
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}
