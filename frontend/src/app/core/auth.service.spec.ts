import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let ctrl: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    auth = TestBed.inject(AuthService);
    ctrl = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  const login = () => {
    auth.user.set({ usuario: 'maria', perfil: 'Admin' });
    auth.ready.set(true);
  };

  it('boot: 401 no /me = sem sessão (ready) ; 500 = servidor indisponível (NÃO ready)', async () => {
    const a = auth.ensureLoaded();
    await Promise.resolve();
    ctrl.expectOne('/api/auth/me').flush({}, { status: 500, statusText: 'Server Error' });
    await a;
    expect(auth.bootError()).toBe(true);
    expect(auth.ready()).toBe(false);

    const b = auth.ensureLoaded(); // nova tentativa
    await Promise.resolve();
    ctrl.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await b;
    expect(auth.bootError()).toBe(false);
    expect(auth.ready()).toBe(true);
    expect(auth.isLoggedIn()).toBe(false);
  });

  it('boot: 200 com formato inesperado (HTML de proxy) NÃO vira sessão', async () => {
    const a = auth.ensureLoaded();
    await Promise.resolve();
    ctrl.expectOne('/api/auth/me').flush('<html>fallback</html>');
    await a;
    expect(auth.isLoggedIn()).toBe(false);
    expect(auth.bootError()).toBe(true);
    expect(auth.ready()).toBe(false);
  });

  it('logout: 401 conta como sucesso e limpa o estado', () => {
    login();
    let done = false;
    auth.logout().subscribe(() => (done = true));
    ctrl.expectOne('/api/auth/logout').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(done).toBe(true);
    expect(auth.isLoggedIn()).toBe(false);
  });

  it('logout: erro de servidor NÃO limpa o estado (a sessão segue viva no servidor)', () => {
    login();
    let status = 0;
    auth.logout().subscribe({ error: (e) => (status = e.status) });
    ctrl.expectOne('/api/auth/logout').flush({}, { status: 500, statusText: 'Server Error' });
    expect(status).toBe(500);
    expect(auth.isLoggedIn()).toBe(true);
  });

  it('401 em paralelo: só o primeiro navega, com returnUrl e aviso', () => {
    login();
    vi.spyOn(router, 'url', 'get').mockReturnValue('/products?q=a');
    auth.sessionExpired();
    auth.sessionExpired(); // segundo 401 (ex.: o dashboard faz 2 requisições)
    expect(router.navigate).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/products?q=a', expirada: 1 } });
  });

  it('perfil desconhecido falha fechado (não é Admin)', () => {
    auth.user.set({ usuario: 'x', perfil: 'Outro' as never });
    expect(auth.canWrite()).toBe(false);
  });
});
