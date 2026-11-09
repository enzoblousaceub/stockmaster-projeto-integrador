import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, firstValueFrom, tap } from 'rxjs';
import { Me } from '../models/auth.model';
import { PUBLIC_AUTH } from './http-context';

const PUBLIC = () => new HttpContext().set(PUBLIC_AUTH, true);

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  /** Sessão atual (null = sem sessão). */
  readonly user = signal<Me | null>(null);
  /** true depois do primeiro GET /api/auth/me (boot). */
  readonly ready = signal(false);

  readonly isLoggedIn = computed(() => this.user() !== null);
  readonly isAdmin = computed(() => this.user()?.perfil === 'Admin');
  /** Escrita em produtos: só Admin (o backend é a autoridade; aqui só escondemos os botões). */
  readonly canWrite = this.isAdmin;

  private loading?: Promise<void>;

  /** Descobre a sessão uma única vez (boot do app / guardas). */
  ensureLoaded(): Promise<void> {
    if (this.ready()) return Promise.resolve();
    return (this.loading ??= this.fetchMe());
  }

  private async fetchMe(): Promise<void> {
    try {
      this.user.set(await firstValueFrom(this.http.get<Me>('/api/auth/me', { context: PUBLIC() })));
    } catch {
      this.user.set(null);
    } finally {
      this.ready.set(true);
    }
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
    return this.http.post<void>('/api/auth/logout', null).pipe(tap(() => this.clear()));
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
    const wasLogged = this.isLoggedIn();
    this.clear();
    const url = this.router.url;
    if (url.startsWith('/login') || url.startsWith('/ativar')) return;
    this.router.navigate(['/login'], { queryParams: wasLogged && url !== '/' ? { returnUrl: url, expirada: 1 } : { expirada: wasLogged ? 1 : null } });
  }
}
