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
