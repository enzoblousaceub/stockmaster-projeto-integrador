import { Component, inject, signal } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { CreatedUser, Perfil } from '../../models/auth.model';
import { UserService } from '../../services/user.service';
import { messageFor, parseProblem } from '../../core/api-error';
import { DIALOG_STYLES } from './dialog-styles';
import { PERFIS } from './perfis';

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
