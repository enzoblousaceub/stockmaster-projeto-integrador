import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';
import { messageFor, parseProblem } from '../../core/api-error';
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
                (change)="theme.set(o.value)" [attr.cdkFocusInitial]="theme.mode() === o.value ? '' : null">
              <mat-icon aria-hidden="true">{{ o.icon }}</mat-icon>
              <span>{{ o.label }}</span>
            </label>
          }
        </div>
      </section>

      @if (auth.user(); as me) {
        <section class="setting" aria-labelledby="sec-account">
          <h3 id="sec-account">Conta</h3>
          <div class="kv"><span>Usuário</span><b>{{ me.usuario }}</b></div>
          <div class="kv"><span>Perfil</span><span class="chip" [class.chip-admin]="me.perfil === 'Admin'">{{ me.perfil }}</span></div>

          <form class="pw" (submit)="$event.preventDefault(); changePassword()" novalidate>
            <h4>Alterar senha</h4>
            @if (pwError()) { <div class="alert" role="alert">{{ pwError() }}</div> }
            @if (pwOk()) { <div class="ok" role="status">Senha alterada. Suas outras sessões foram encerradas.</div> }
            <div class="field">
              <label for="pw-atual">Senha atual</label>
              <input #a id="pw-atual" type="password" autocomplete="current-password" [value]="atual()" (input)="atual.set(a.value)">
            </div>
            <div class="field">
              <label for="pw-nova">Nova senha</label>
              <input #n id="pw-nova" type="password" autocomplete="new-password" maxlength="128" aria-describedby="pw-hint"
                [value]="nova()" (input)="nova.set(n.value)">
              <span class="hint" id="pw-hint">De 8 a 128 caracteres. Ao trocar, suas outras sessões são encerradas.</span>
            </div>
            <button type="submit" class="btn btn-outline" [disabled]="pwLoading()" id="btn-change-password">
              <mat-icon aria-hidden="true">key</mat-icon><span>{{ pwLoading() ? 'Alterando…' : 'Alterar senha' }}</span>
            </button>
          </form>

          <div class="logout">
            <button type="button" class="btn btn-outline" (click)="logout()" [disabled]="outLoading()" id="btn-logout">
              <mat-icon aria-hidden="true">logout</mat-icon><span>Sair de todos os dispositivos</span>
            </button>
            <p class="hint">Encerra a sua sessão neste e em todos os outros dispositivos.</p>
          </div>
        </section>
      }
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
    h4 { margin: 16px 0 8px; font-size: 0.9375rem; font-weight: 600; }
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
    .seg:focus-within { outline: 2px solid var(--sm-primary); outline-offset: 2px; }
    .seg:has(input:not(:focus-visible)) { outline: none; }

    .kv { display: flex; justify-content: space-between; gap: 12px; padding: 6px 0; }
    .kv span:first-child { color: var(--sm-text-2); }
    .chip { padding: 2px 10px; border-radius: 999px; font-size: 0.75rem; font-weight: 600; background: var(--sm-surface-2); border: 1px solid var(--sm-border); color: var(--sm-text-2); }
    .chip-admin { background: var(--sm-primary-soft); color: var(--sm-primary); border-color: transparent; }
    .field { display: flex; flex-direction: column; gap: 6px; margin-bottom: 12px; }
    .field label { font-size: 0.875rem; font-weight: 500; }
    .field input { height: 48px; box-sizing: border-box; padding: 0 12px; border: 1px solid var(--sm-control-border); border-radius: var(--sm-radius); background: var(--sm-surface-2); color: var(--sm-text); font: inherit; }
    .field input:focus-visible { outline: 2px solid var(--sm-primary); outline-offset: 1px; background: var(--sm-surface); }
    .field .hint { margin: 0; font-size: 0.8125rem; }
    .alert, .ok { padding: 10px 12px; margin-bottom: 12px; border-radius: var(--sm-radius); font-size: 0.875rem; }
    .alert { background: var(--sm-danger-soft); color: var(--sm-danger); }
    .ok { background: var(--sm-success-soft); color: var(--sm-success); }
    .btn { display: inline-flex; align-items: center; justify-content: center; gap: 8px; width: 100%; min-height: 48px; padding: 0 16px; border-radius: var(--sm-radius); font: inherit; font-weight: 600; cursor: pointer; }
    .btn mat-icon { width: 20px; height: 20px; font-size: 20px; }
    .btn-outline { border: 1px solid var(--sm-control-border); background: transparent; color: var(--sm-text); }
    .btn-outline:hover:not(:disabled) { background: var(--sm-surface-2); }
    .btn:disabled { opacity: 0.6; cursor: progress; }
    .logout { margin-top: 20px; padding-top: 16px; border-top: 1px solid var(--sm-border); }
    .logout .hint { margin: 8px 0 0; }
  `]
})
export class SettingsDialogComponent {
  readonly theme = inject(ThemeService);
  readonly auth = inject(AuthService);
  private router = inject(Router);
  private ref = inject(MatDialogRef<SettingsDialogComponent>);

  readonly themes: { value: ThemeMode; label: string; icon: string }[] = [
    { value: 'light', label: 'Claro', icon: 'light_mode' },
    { value: 'dark', label: 'Escuro', icon: 'dark_mode' }
  ];

  readonly atual = signal('');
  readonly nova = signal('');
  readonly pwLoading = signal(false);
  readonly pwError = signal('');
  readonly pwOk = signal(false);
  readonly outLoading = signal(false);

  changePassword(): void {
    if (this.pwLoading()) return;
    this.pwError.set('');
    this.pwOk.set(false);
    if (!this.atual()) { this.pwError.set('Informe a senha atual.'); return; }
    if (this.nova().length < 8 || this.nova().length > 128) {
      this.pwError.set('A nova senha deve ter entre 8 e 128 caracteres.');
      return;
    }
    this.pwLoading.set(true);
    this.auth.changePassword(this.atual(), this.nova()).subscribe({
      next: () => {
        this.pwLoading.set(false);
        this.atual.set('');
        this.nova.set('');
        this.pwOk.set(true);
      },
      error: (err) => {
        this.pwLoading.set(false);
        this.pwError.set(messageFor(parseProblem(err)));
      }
    });
  }

  /** Encerra TODAS as sessões da pessoa. Mesmo se a chamada falhar, limpa o estado local. */
  logout(): void {
    if (this.outLoading()) return;
    this.outLoading.set(true);
    const done = () => {
      this.auth.clear();
      this.ref.close();
      this.router.navigate(['/login']);
    };
    this.auth.logout().subscribe({ next: done, error: done });
  }
}
