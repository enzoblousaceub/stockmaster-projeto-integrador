import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../../core/auth.service';
import { messageFor, parseProblem } from '../../core/api-error';
import { ActivationCode, ManagedUser } from '../../models/auth.model';
import { UserService } from '../../services/user.service';
import { ConfirmDialogComponent } from '../../components/confirm-dialog/confirm-dialog';
import { CodeDialogComponent } from '../../components/code-dialog/code-dialog';
import { CreateUserDialogComponent } from '../../components/user-dialogs/create-user-dialog';
import { EditProfileDialogComponent } from '../../components/user-dialogs/edit-profile-dialog';

@Component({
  selector: 'app-users',
  standalone: true,
  imports: [MatIconModule, DatePipe],
  templateUrl: './users.html',
  styleUrl: './users.scss'
})
export class UsersComponent implements OnInit {
  private users = inject(UserService);
  private auth = inject(AuthService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);

  readonly state = signal<'loading' | 'error' | 'ready'>('loading');
  readonly list = signal<ManagedUser[]>([]);
  readonly announcement = signal('');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.fetch();
  }

  private fetch(): void {
    this.users.list().subscribe({
      next: (l) => { this.list.set(l); this.state.set('ready'); },
      error: () => { if (this.state() === 'loading') this.state.set('error'); }
    });
  }

  isSelf(u: ManagedUser): boolean {
    return this.auth.user()?.email === u.email;
  }

  isLocked(u: ManagedUser): boolean {
    return !!u.bloqueadoAte && new Date(u.bloqueadoAte).getTime() > Date.now();
  }

  /** Horas restantes do código (arredondado para cima); null se não há código */
  codeHours(u: ManagedUser): number | null {
    if (!u.codigoExpiraEm) return null;
    const h = (new Date(u.codigoExpiraEm).getTime() - Date.now()) / 3_600_000;
    return h <= 0 ? 0 : Math.ceil(h);
  }

  initials(u: ManagedUser): string {
    return u.email.slice(0, 2).toUpperCase();
  }

  // ---------- ações ----------
  create(): void {
    this.dialog.open(CreateUserDialogComponent, {
      width: '460px', maxWidth: 'calc(100vw - 32px)', ariaLabelledBy: 'create-title'
    }).afterClosed().subscribe((created) => {
      if (!created) return;
      this.deliver(`Usuário ${created.email} criado`, created.email, created);
      this.fetch();
    });
  }

  editProfile(u: ManagedUser): void {
    this.dialog.open(EditProfileDialogComponent, {
      width: '460px', maxWidth: 'calc(100vw - 32px)', ariaLabelledBy: 'edit-title', data: { user: u }
    }).afterClosed().subscribe((updated) => {
      if (!updated) return;
      if (updated.emailEnviado != null) this.deliver(`Novo convite para ${updated.email}`, updated.email, updated as ActivationCode);
      else this.say(`${u.email} atualizado.`);
      this.fetch();
    });
  }

  reset(u: ManagedUser): void {
    const pending = u.status === 'Pendente';
    this.confirm({
      title: pending ? `Gerar novo código para ${u.email}?` : `Redefinir o acesso de ${u.email}?`,
      message: pending
        ? 'O código anterior deixa de valer e um novo será gerado. Ele é mostrado uma única vez, na próxima tela.'
        : 'A senha atual deixa de valer, as sessões abertas são encerradas e a pessoa volta a Pendente. Um novo código de ativação será gerado e mostrado uma única vez.',
      confirmLabel: pending ? 'Gerar novo código' : 'Redefinir acesso',
      icon: 'key'
    }, () => this.users.reset(u.id).subscribe({
      next: (r) => {
        this.deliver(`Novo código para ${u.email}`, u.email, r);
        this.fetch();
      },
      error: (err) => this.fail(err)
    }));
  }

  deactivate(u: ManagedUser): void {
    this.confirm({
      title: `Desativar ${u.email}?`,
      message: 'A pessoa perde o acesso e as sessões abertas são encerradas na hora. Você pode reativar depois.',
      confirmLabel: 'Desativar',
      icon: 'block',
      danger: true
    }, () => this.users.patch(u.id, { ativo: false }).subscribe({
      next: () => { this.say(`${u.email} desativado.`); this.fetch(); },
      error: (err) => this.fail(err)
    }));
  }

  reactivate(u: ManagedUser): void {
    this.users.patch(u.id, { ativo: true }).subscribe({
      next: (r) => {
        if (r.codigoAtivacao || r.emailEnviado) {
          this.deliver(`${u.email} reativado`, u.email, r);
        } else {
          this.say(`${u.email} reativado.`);
        }
        this.fetch();
      },
      error: (err) => this.fail(err)
    });
  }

  unlock(u: ManagedUser): void {
    this.users.unlock(u.id).subscribe({
      next: () => { this.say(`${u.email} desbloqueado.`); this.fetch(); },
      error: (err) => this.fail(err)
    });
  }

  // ---------- helpers ----------
  private confirm(data: { title: string; message: string; confirmLabel: string; icon: string; danger?: boolean }, then: () => void): void {
    this.dialog.open(ConfirmDialogComponent, {
      width: '440px', maxWidth: 'calc(100vw - 32px)', ariaLabelledBy: 'confirm-title', ariaDescribedBy: 'confirm-desc', data
    }).afterClosed().subscribe((ok) => ok && then());
  }

  /** Convite enviado por e-mail: só avisa. Falha no envio: mostra o código para o Admin repassar. */
  private deliver(title: string, email: string, r: ActivationCode): void {
    if (r.emailEnviado) {
      this.say(`Convite enviado por e-mail para ${email}.`);
      return;
    }
    if (r.emailEnviado === false) this.snack.open('Não foi possível enviar o e-mail. Repasse o código manualmente.', 'Fechar', { duration: 6000 });
    if (r.codigoAtivacao) this.showCode(title, email, r.codigoAtivacao, r.codigoExpiraEm);
  }

  private showCode(title: string, usuario: string, code: string, expiresAt: string): void {
    this.dialog.open(CodeDialogComponent, {
      width: '480px', maxWidth: 'calc(100vw - 32px)', ariaLabelledBy: 'code-title',
      disableClose: true, data: { title, usuario, code, expiresAt }
    });
  }

  private say(msg: string): void {
    this.announcement.set(msg);
    this.snack.open(msg, 'Fechar', { duration: 3000 });
  }

  private fail(err: unknown): void {
    const p = parseProblem(err);
    if (p.status === 403 && p.code !== 'csrf_invalid') return; // o interceptor já avisou (sem permissão)
    this.snack.open(messageFor(p), 'Fechar', { duration: 5000 });
  }
}
