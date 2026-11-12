import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../core/auth.service';
import { formatWait, messageFor, parseProblem, safeReturnUrl } from '../../core/api-error';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [RouterLink, MatIconModule],
  templateUrl: './login.html',
  styleUrl: './auth.scss'
})
export class LoginComponent implements OnDestroy {
  private auth = inject(AuthService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  readonly email = signal('');
  readonly senha = signal('');
  readonly showPassword = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  /** segundos restantes de bloqueio/limite (429) */
  readonly wait = signal(0);
  readonly waitText = computed(() => formatWait(this.wait()));
  /** texto lido por leitores de tela: só no início e no fim da espera */
  readonly waitNote = signal('');

  readonly activated = this.route.snapshot.queryParamMap.get('ativado') === '1';
  readonly expired = this.route.snapshot.queryParamMap.get('expirada') === '1';

  private timer?: ReturnType<typeof setInterval>;

  submit(): void {
    if (this.loading() || this.wait() > 0) return;
    const email = this.email().trim();
    const senha = this.senha();
    if (!email || !senha) {
      this.error.set('Informe o usuário e a senha.');
      return;
    }
    this.error.set('');
    this.loading.set(true);
    this.auth.login(email, senha).subscribe({
      next: () => {
        this.loading.set(false);
        this.senha.set('');
        this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl')));
      },
      error: (err) => {
        this.loading.set(false);
        this.senha.set('');
        const p = parseProblem(err);
        // o tempo restante aparece na contagem, então tira da mensagem
        this.error.set(messageFor({ ...p, retryAfterSeconds: undefined }));
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
      if (this.wait() <= 0) {
        clearInterval(this.timer);
        this.error.set('');
        this.waitNote.set('Você já pode tentar novamente.');
      }
    }, 1000);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}
