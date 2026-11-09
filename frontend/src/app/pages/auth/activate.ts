import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';
import { formatWait, messageFor, parseProblem } from '../../core/api-error';

@Component({
  selector: 'app-activate',
  standalone: true,
  imports: [RouterLink, MatIconModule],
  templateUrl: './activate.html',
  styleUrl: './auth.scss'
})
export class ActivateComponent implements OnDestroy {
  private auth = inject(AuthService);
  private router = inject(Router);

  readonly usuario = signal('');
  readonly codigo = signal('');
  readonly novaSenha = signal('');
  readonly repetir = signal('');
  readonly showPassword = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly passwordError = signal('');
  readonly wait = signal(0);
  readonly waitText = computed(() => formatWait(this.wait()));
  /** texto lido por leitores de tela: só no início e no fim da espera */
  readonly waitNote = signal('');

  private timer?: ReturnType<typeof setInterval>;

  submit(): void {
    if (this.loading() || this.wait() > 0) return;
    this.error.set('');
    this.passwordError.set('');
    const usuario = this.usuario().trim();
    const codigo = this.codigo().trim();
    const nova = this.novaSenha();

    if (!usuario || !codigo) {
      this.error.set('Informe o usuário e o código de ativação.');
      return;
    }
    if (nova.length < 8 || nova.length > 128) {
      this.passwordError.set('A senha deve ter entre 8 e 128 caracteres.');
      return;
    }
    if (nova !== this.repetir()) {
      this.passwordError.set('As senhas não conferem.');
      return;
    }

    this.loading.set(true);
    this.auth.activate(usuario, codigo, nova).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/login'], { queryParams: { ativado: 1 } });
      },
      error: (err) => {
        this.loading.set(false);
        const p = parseProblem(err);
        const text = messageFor({ ...p, retryAfterSeconds: undefined });
        // erros da senha ficam no campo (o código continua válido e pode ser reenviado)
        if (p.code === 'password_too_short' || p.code === 'password_weak') this.passwordError.set(text);
        else this.error.set(text);
        if (p.status === 429 && p.retryAfterSeconds) this.startCountdown(p.retryAfterSeconds);
      }
    });
  }

  private startCountdown(seconds: number): void {
    clearInterval(this.timer);
    this.wait.set(seconds);
    this.waitNote.set(`Tente novamente em ${formatWait(seconds)}.`);
    this.timer = setInterval(() => {
      this.wait.update((n) => n - 1);
      if (this.wait() <= 0) { clearInterval(this.timer); this.error.set(''); this.waitNote.set('Você já pode tentar novamente.'); }
    }, 1000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}
