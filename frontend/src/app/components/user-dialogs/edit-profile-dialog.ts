import { Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { ManagedUser, PatchedUser, Perfil } from '../../models/auth.model';
import { UserService } from '../../services/user.service';
import { messageFor, parseProblem } from '../../core/api-error';
import { DIALOG_STYLES } from './dialog-styles';
import { PERFIS } from './perfis';

@Component({
  selector: 'app-edit-profile-dialog',
  standalone: true,
  imports: [MatDialogModule],
  template: `
    <h2 mat-dialog-title id="edit-title">Editar {{ data.user.email }}</h2>
    <form (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content>
        <p class="sub">Ao mudar o perfil, as sessões abertas desta pessoa são encerradas.</p>
        @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }
        @if (data.user.status === 'Pendente') {
          <div class="field">
            <label for="edit-email">E-mail</label>
            <input #e id="edit-email" type="email" inputmode="email" autocomplete="off" autocapitalize="none" spellcheck="false" maxlength="254"
              aria-describedby="edit-email-hint" [value]="email()" (input)="email.set(e.value)">
            <span class="hint" id="edit-email-hint">Ao trocar o e-mail, um novo convite é enviado ao novo endereço e o anterior deixa de valer.</span>
          </div>
        }
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
  readonly email = signal(this.data.user.email);
  readonly loading = signal(false);
  readonly error = signal('');

  submit(): void {
    if (this.loading()) return;
    this.error.set('');
    const body: { perfil?: Perfil; email?: string } = {};
    if (this.perfil() !== this.data.user.perfil) body.perfil = this.perfil();
    const email = this.email().trim().toLowerCase();
    if (email !== this.data.user.email) {
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) || email.length > 254) {
        this.error.set('Informe um e-mail válido.');
        return;
      }
      body.email = email;
    }
    if (!body.perfil && !body.email) { this.ref.close(); return; }
    this.loading.set(true);
    this.users.patch(this.data.user.id, body).subscribe({
      next: (u) => this.ref.close(u),
      error: (err) => {
        this.loading.set(false);
        this.error.set(messageFor(parseProblem(err)));
      }
    });
  }
}
