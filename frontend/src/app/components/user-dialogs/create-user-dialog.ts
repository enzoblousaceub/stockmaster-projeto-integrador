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
        <p class="sub">A pessoa recebe um convite por e-mail para criar a própria senha.</p>
        @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }
        <div class="field">
          <label for="new-email">E-mail</label>
          <input #e id="new-email" type="email" inputmode="email" autocomplete="off" autocapitalize="none" spellcheck="false" maxlength="254"
            [attr.aria-invalid]="error() ? 'true' : null" [value]="email()" (input)="email.set(e.value)">
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
        <button type="submit" class="btn btn-primary" [disabled]="loading()" id="btn-create-user">{{ loading() ? 'Enviando…' : 'Criar e enviar convite' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: [DIALOG_STYLES]
})
export class CreateUserDialogComponent {
  private users = inject(UserService);
  private ref = inject<MatDialogRef<CreateUserDialogComponent, CreatedUser>>(MatDialogRef);

  readonly perfis = PERFIS;
  readonly email = signal('');
  readonly perfil = signal<Perfil>('Visualizador');
  readonly loading = signal(false);
  readonly error = signal('');

  submit(): void {
    if (this.loading()) return;
    const email = this.email().trim().toLowerCase();
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 254) {
      this.error.set('Informe um e-mail válido.');
      return;
    }
    this.error.set('');
    this.loading.set(true);
    this.users.create(email, this.perfil()).subscribe({
      next: (created) => this.ref.close(created),
      error: (err) => {
        this.loading.set(false);
        this.error.set(messageFor(parseProblem(err)));
      }
    });
  }
}
