import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpContext, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, firstValueFrom, of, tap, throwError } from 'rxjs';
import { Me } from '../models/auth.model';
import { NO_401_REDIRECT, PUBLIC_AUTH } from './http-context';

const PUBLIC = () => new HttpContext().set(PUBLIC_AUTH, true);

/** Resposta de /me só vale com o formato do contrato (um HTML 200 de proxy/fallback não é sessão). */
function isMe(v: unknown): v is Me {
  const m = v as Partial<Me> | null;
  return !!m && typeof m.usuario === 'string' && (m.perfil === 'Admin' || m.perfil === 'Visualizador');
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  /** Sessão atual (null = sem sessão). */
  readonly user = signal<Me | null>(null);
  /** true depois do primeiro GET /api/auth/me bem-sucedido (200 ou 401). */
  readonly ready = signal(false);
  /** servidor indisponível no boot (rede/5xx): NÃO é "sem sessão". */
  readonly bootError = signal(false);

  readonly isLoggedIn = computed(() => this.user() !== null);
  readonly isAdmin = computed(() => this.user()?.perfil === 'Admin');
  /** Escrita em produtos: só Admin (o backend é a autoridade; aqui só escondemos os botões). */
  readonly canWrite = this.isAdmin;

  private loading?: Promise<void>;

  /** Avisa as outras abas do mesmo navegador (só o sinal "logout"; nenhum dado de sessão trafega). */
  private channel: BroadcastChannel | null = null;

  constructor() {
    if (typeof BroadcastChannel !== 'undefined') {
      this.channel = new BroadcastChannel('sm-auth');
      this.channel.onmessage = (e) => { if (e.data === 'logout') this.remoteLogout(); };
    }
  }

  /** Outra aba fez logout (em todos os dispositivos): esta aba também sai. */
  private remoteLogout(): void {
    if (!this.isLoggedIn()) return;
    this.clear();
    const url = this.router.url;
    if (!url.startsWith('/login') && !url.startsWith('/ativar')) this.router.navigate(['/login']);
  }

  /** Descobre a sessão uma única vez (boot do app / guardas). */
  ensureLoaded(): Promise<void> {
    if (this.ready()) return Promise.resolve();
    return (this.loading ??= this.fetchMe());
  }

  private async fetchMe(): Promise<void> {
    try {
      const me = await firstValueFrom(this.http.get<Me>('/api/auth/me', { context: PUBLIC() }));
      if (!isMe(me)) throw new Error('Resposta inesperada de /api/auth/me');
      this.user.set(me);
      this.bootError.set(false);
      this.ready.set(true);
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 401) {
        // sem sessão (normal)
        this.user.set(null);
        this.bootError.set(false);
        this.ready.set(true);
      } else {
        // rede/5xx: não decide "sem sessão"; mostra "servidor indisponível" com nova tentativa
        this.bootError.set(true);
        this.loading = undefined;
      }
    }
  }

  /** Nova tentativa do boot depois de uma falha de rede/servidor. */
  async retryBoot(): Promise<void> {
    this.bootError.set(false);
    this.loading = undefined;
    await this.ensureLoaded();
    if (this.ready()) this.router.navigateByUrl(location.pathname + location.search, { replaceUrl: true });
  }

  /** Renova o cookie XSRF-TOKEN (usado no retry de csrf_invalid). */
  refresh(): Observable<Me> {
    return this.http.get<Me>('/api/auth/me').pipe(tap((me) => this.user.set(me)));
  }

  login(usuario: string, senha: string): Observable<Me> {
    return this.http
      .post<Me>('/api/auth/login', { usuario, senha }, { context: PUBLIC() })
      .pipe(tap((me) => { this.user.set(me); this.ready.set(true); }));
  }

  /** Encerra a sessão em TODOS os dispositivos (decisão do produto). */
  logout(): Observable<void> {
    // 401 = a sessão já não existe: conta como sucesso. Outros erros sobem (a tela NÃO finge que saiu).
    return this.http.post<void>('/api/auth/logout', null, { context: new HttpContext().set(NO_401_REDIRECT, true) }).pipe(
      catchError((err) => (err instanceof HttpErrorResponse && err.status === 401 ? of(undefined) : throwError(() => err))),
      tap(() => { this.clear(); this.channel?.postMessage('logout'); })
    );
  }

  changePassword(senhaAtual: string, novaSenha: string): Observable<void> {
    return this.http.post<void>('/api/auth/senha', { senhaAtual, novaSenha });
  }

  activate(usuario: string, codigo: string, novaSenha: string): Observable<void> {
    return this.http.post<void>('/api/auth/ativar', { usuario, codigo, novaSenha }, { context: PUBLIC() });
  }

  clear(): void {
    this.user.set(null);
    this.ready.set(true);
  }

  /** 401 fora de login/ativar: limpa o estado e volta ao login guardando o destino. */
  sessionExpired(): void {
    // 401s em paralelo: só o primeiro (com sessão) navega; os demais não sobrescrevem returnUrl/aviso
    if (!this.isLoggedIn()) return;
    this.clear();
    const url = this.router.url;
    if (url.startsWith('/login') || url.startsWith('/ativar')) return;
    this.router.navigate(['/login'], { queryParams: url !== '/' ? { returnUrl: url, expirada: 1 } : { expirada: 1 } });
  }
}
