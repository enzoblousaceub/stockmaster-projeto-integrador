import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

/** Confirmação de exclusão. Foco inicial no "Cancelar" (ação segura); ESC cancela. */
@Component({
  selector: 'app-delete-dialog',
  standalone: true,
  imports: [MatDialogModule, MatIconModule],
  template: `
    <div class="head">
      <span class="icon" aria-hidden="true"><mat-icon>delete_forever</mat-icon></span>
      <h2 mat-dialog-title id="delete-title">Excluir produto?</h2>
    </div>
    <mat-dialog-content>
      <p id="delete-desc">
        O produto <strong>{{ data.name }}</strong> será excluído permanentemente. Esta ação não pode ser desfeita.
      </p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" class="btn btn-outline" [mat-dialog-close]="false" cdkFocusInitial id="btn-dialog-cancel">
        Cancelar
      </button>
      <button type="button" class="btn btn-danger" [mat-dialog-close]="true" id="btn-dialog-confirm">
        <mat-icon aria-hidden="true">delete</mat-icon>
        <span>Excluir</span>
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    :host { display: block; color: var(--sm-text); }
    .head { display: flex; align-items: center; gap: 12px; padding: 4px 24px 0; }
    .head h2 { margin: 0; padding: 0; flex: 1; font-size: 1.125rem; font-weight: 600; }
    .head h2::before { display: none; }
    .icon {
      display: inline-flex; align-items: center; justify-content: center;
      width: 40px; height: 40px; border-radius: 50%;
      background: var(--sm-danger-soft); color: var(--sm-danger);
    }
    p { margin: 8px 0 0; color: var(--sm-text-2); overflow-wrap: anywhere; }
    p strong { color: var(--sm-text); }
    mat-dialog-actions { gap: 8px; padding: 12px 24px 20px; }
    .btn {
      display: inline-flex; align-items: center; justify-content: center; gap: 8px;
      min-height: 48px; padding: 0 20px; border-radius: var(--sm-radius);
      font: inherit; font-weight: 600; cursor: pointer;
    }
    .btn mat-icon { width: 20px; height: 20px; font-size: 20px; }
    .btn-outline { border: 1px solid var(--sm-control-border); background: transparent; color: var(--sm-text); }
    .btn-outline:hover { background: var(--sm-surface-2); }
    .btn-danger { border: 1px solid var(--sm-danger); background: var(--sm-danger); color: #fff; }
    .btn-danger:hover { filter: brightness(1.1); }
    @media (max-width: 480px) {
      mat-dialog-actions { flex-direction: column-reverse; align-items: stretch; }
    }
  `]
})
export class DeleteDialogComponent {
  readonly data = inject<{ name: string }>(MAT_DIALOG_DATA);
}
