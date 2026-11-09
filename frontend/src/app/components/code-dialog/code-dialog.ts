import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

export interface CodeDialogData {
  title: string;
  usuario: string;
  code: string;
  expiresAt: string;
}

/**
 * Mostra o código de ativação UMA única vez (o servidor guarda só o HMAC).
 * Não fecha por ESC/clique fora: o Admin precisa copiar e confirmar em "Concluir".
 */
@Component({
  selector: 'app-code-dialog',
  standalone: true,
  imports: [MatDialogModule, MatIconModule, DatePipe],
  template: `
    <div class="ok" aria-hidden="true"><span class="big"><mat-icon>key</mat-icon></span></div>
    <h2 mat-dialog-title id="code-title">{{ data.title }}</h2>
    <mat-dialog-content>
      <p class="lead">
        Repasse este código a <b>{{ data.usuario }}</b>. Ele vale até
        <b>{{ data.expiresAt | date: 'dd/MM/yyyy HH:mm' }}</b> (48 horas), serve uma única vez e
        <b>não será exibido de novo</b>.
      </p>
      <div class="code-box">
        <code id="activation-code" class="num" tabindex="0" aria-label="Código de ativação">{{ data.code }}</code>
        <button type="button" class="btn btn-outline" (click)="copy()" id="btn-copy">
          <mat-icon aria-hidden="true">{{ copied() ? 'check' : 'content_copy' }}</mat-icon>
          <span>{{ copied() ? 'Copiado' : 'Copiar' }}</span>
        </button>
      </div>
      <div class="sr-only" role="status" aria-live="polite">{{ copied() ? 'Código copiado' : '' }}</div>
      @if (failed()) { <p class="hint" role="alert">Não foi possível copiar automaticamente. Selecione o código e copie manualmente.</p> }
      <p class="hint">{{ data.usuario }} ativa o acesso em <b>/ativar</b>, com o usuário, o código e uma nova senha.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" class="btn btn-primary" mat-dialog-close cdkFocusInitial id="btn-code-done">Concluir</button>
    </mat-dialog-actions>
  `,
  styles: [`
    :host { display: block; color: var(--sm-text); }
    .ok { text-align: center; padding-top: 8px; }
    .big { display: inline-flex; width: 56px; height: 56px; border-radius: 50%; align-items: center; justify-content: center; background: var(--sm-success-soft); color: var(--sm-success); }
    .big mat-icon { width: 30px; height: 30px; font-size: 30px; }
    h2 { text-align: center; font-size: 1.25rem; font-weight: 600; }
    .lead { margin: 0 0 12px; color: var(--sm-text-2); text-align: center; }
    .code-box { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px; padding: 14px 16px; margin-bottom: 12px; border: 1px dashed var(--sm-primary); border-radius: var(--sm-radius); background: var(--sm-primary-soft); }
    code { font-size: 1.5rem; font-weight: 600; letter-spacing: 0.06em; color: var(--sm-primary); user-select: all; }
    .hint { margin: 0 0 8px; color: var(--sm-text-2); font-size: 0.875rem; }
    mat-dialog-actions { padding: 8px 24px 20px; }
    .btn { display: inline-flex; align-items: center; justify-content: center; gap: 8px; min-height: 48px; padding: 0 18px; border-radius: var(--sm-radius); font: inherit; font-weight: 600; cursor: pointer; }
    .btn mat-icon { width: 20px; height: 20px; font-size: 20px; }
    .btn-outline { border: 1px solid var(--sm-control-border); background: var(--sm-surface); color: var(--sm-text); }
    .btn-primary { border: 1px solid var(--sm-primary); background: var(--sm-primary); color: var(--sm-on-primary); }
  `]
})
export class CodeDialogComponent {
  readonly data = inject<CodeDialogData>(MAT_DIALOG_DATA);
  readonly copied = signal(false);
  readonly failed = signal(false);

  async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.data.code);
      this.copied.set(true);
      this.failed.set(false);
    } catch {
      this.failed.set(true);
    }
  }
}
