import { Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { CreatedUser, ManagedUser, PatchedUser, Perfil } from '../../models/auth.model';
import { UserService } from '../../services/user.service';
import { messageFor, parseProblem } from '../../core/api-error';

const DIALOG_STYLES = `
  :host { display: block; color: var(--sm-text); }
  h2 { font-size: 1.25rem; font-weight: 600; }
  .sub { margin: 0 0 16px; color: var(--sm-text-2); }
  .field { display: flex; flex-direction: column; gap: 6px; margin-bottom: 16px; }
  label { font-size: 0.875rem; font-weight: 500; }
  input, select { width: 100%; box-sizing: border-box; height: 52px; padding: 0 14px; border: 1px solid var(--sm-control-border); border-radius: var(--sm-radius); background: var(--sm-surface-2); color: var(--sm-text); font: inherit; }
  input:focus-visible, select:focus-visible { outline: 2px solid var(--sm-primary); outline-offset: 1px; background: var(--sm-surface); }
  input[aria-invalid='true'] { border-color: var(--sm-danger); }
  .hint { color: var(--sm-text-2); font-size: 0.8125rem; }
  .alert { display: flex; gap: 10px; padding: 12px 14px; margin-bottom: 16px; border-radius: var(--sm-radius); background: var(--sm-danger-soft); color: var(--sm-danger); font-size: 0.9375rem; }
  mat-dialog-actions { gap: 8px; padding: 8px 24px 20px; }
  .btn { display: inline-flex; align-items: center; justify-content: center; min-height: 48px; padding: 0 20px; border-radius: var(--sm-radius); font: inherit; font-weight: 600; cursor: pointer; }
  .btn-outline { border: 1px solid var(--sm-control-border); background: transparent; color: var(--sm-text); }
  .btn-primary { border: 1px solid var(--sm-primary); background: var(--sm-primary); color: var(--sm-on-primary); }
  .btn-primary:disabled { opacity: 0.6; cursor: progress; }
  @media (max-width: 480px) { mat-dialog-actions { flex-direction: column-reverse; align-items: stretch; } }
`;

const PERFIS: { value: Perfil; label: string }[] = [
  { value: 'Visualizador', label: 'Visualizador (somente leitura)' },
  { value: 'Admin', label: 'Admin (acesso total)' }
];

@Component({
  selector: 'app-create-user-dialog',
  standalone: true,
  imports: [MatDialogModule],
  template: `
    <h2 mat-dialog-title id="create-title">Novo usuário</h2>
    <form (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content>
        <p class="sub">A pessoa define a própria senha com um código de ativação.</p>
        @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }
        <div class="field">
          <label for="new-usuario">Usuário</label>
          <input #u id="new-usuario" type="text" autocomplete="off" autocapitalize="none" spellcheck="false" maxlength="32"
            aria-describedby="new-usuario-hint" [attr.aria-invalid]="error() ? 'true' : null" [value]="usuario()" (input)="usuario.set(u.value)">
          <span class="hint" id="new-usuario-hint">3 a 32 caracteres: letras minúsculas, números, ponto, hífen ou sublinhado.</span>
        </div>
        <div class="field">
          <label for="new-perfil">Perfil</label>
          <select #p id="new-perfil" [value]="perfil()" (change)="perfil.set($any(p.value))">
            @for (o of perfis; track o.value) { <option [value]="o.value" [selected]="o.value === perfil()">{{ o.label }}</option> }
          </select>
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button type="button" class="btn btn-outline" mat-dialog-close>Cancelar</button>
        <button type="submit" class="btn btn-primary" [disabled]="loading()" id="btn-create-user">{{ loading() ? 'Criando…' : 'Criar usuário' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: [DIALOG_STYLES]
})
export class CreateUserDialogComponent {
  private users = inject(UserService);
  private ref = inject<MatDialogRef<CreateUserDialogComponent, CreatedUser>>(MatDialogRef);

  readonly perfis = PERFIS;
  readonly usuario = signal('');
  readonly perfil = signal<Perfil>('Visualizador');
  readonly loading = signal(false);
  readonly error = signal('');

  submit(): void {
    if (this.loading()) return;
    const usuario = this.usuario().trim().toLowerCase();
    if (!/^[a-z0-9._-]{3,32}$/.test(usuario)) {
      this.error.set('O usuário deve ter de 3 a 32 caracteres: letras minúsculas, números, ponto, hífen ou sublinhado.');
      return;
    }
    this.error.set('');
    this.loading.set(true);
    this.users.create(usuario, this.perfil()).subscribe({
      next: (created) => this.ref.close(created),
      error: (err) => {
        this.loading.set(false);
        this.error.set(messageFor(parseProblem(err)));
      }
    });
  }
}

@Component({
  selector: 'app-edit-profile-dialog',
  standalone: true,
  imports: [MatDialogModule],
  template: `
    <h2 mat-dialog-title id="edit-title">Alterar perfil de {{ data.user.usuario }}</h2>
    <form (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content>
        <p class="sub">As sessões abertas desta pessoa são encerradas ao mudar o perfil.</p>
        @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }
        <div class="field">
          <label for="edit-perfil">Perfil</label>
          <select #p id="edit-perfil" [value]="perfil()" (change)="perfil.set($any(p.value))">
            @for (o of perfis; track o.value) { <option [value]="o.value" [selected]="o.value === perfil()">{{ o.label }}</option> }
          </select>
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button type="button" class="btn btn-outline" mat-dialog-close>Cancelar</button>
        <button type="submit" class="btn btn-primary" [disabled]="loading()" id="btn-save-profile">{{ loading() ? 'Salvando…' : 'Salvar' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: [DIALOG_STYLES]
})
export class EditProfileDialogComponent {
  private users = inject(UserService);
  private ref = inject<MatDialogRef<EditProfileDialogComponent, PatchedUser>>(MatDialogRef);
  readonly data = inject<{ user: ManagedUser }>(MAT_DIALOG_DATA);

  readonly perfis = PERFIS;
  readonly perfil = signal<Perfil>(this.data.user.perfil);
  readonly loading = signal(false);
  readonly error = signal('');

  submit(): void {
    if (this.loading()) return;
    this.error.set('');
    this.loading.set(true);
    this.users.patch(this.data.user.id, { perfil: this.perfil() }).subscribe({
      next: (u) => this.ref.close(u),
      error: (err) => {
        this.loading.set(false);
        this.error.set(messageFor(parseProblem(err)));
      }
    });
  }
}
