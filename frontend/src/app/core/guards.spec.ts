import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { adminGuard, authGuard, guestGuard } from './guards';

const route = {} as ActivatedRouteSnapshot;
const stateOf = (url: string) => ({ url }) as RouterStateSnapshot;

describe('guardas de rota', () => {
  let ctrl: HttpTestingController;
  let router: Router;
  let snack: { open: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    snack = { open: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: snack }
      ]
    });
    ctrl = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  const run = (guard: typeof authGuard, url = '/products') =>
    TestBed.runInInjectionContext(() => guard(route, stateOf(url))) as Promise<boolean | UrlTree>;

  /** responde ao GET /api/auth/me do boot */
  const respondMe = async (p: Promise<unknown>, status: number, body: object) => {
    await Promise.resolve();
    ctrl.expectOne('/api/auth/me').flush(body, { status, statusText: status === 200 ? 'OK' : 'Unauthorized' });
    return p;
  };

  it('authGuard sem sessão (401 no /me) manda ao /login guardando o destino', async () => {
    const p = run(authGuard, '/products?q=mouse');
    const res = await respondMe(p, 401, { code: 'unauthenticated' });
    expect(res).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(res as UrlTree)).toBe('/login?returnUrl=%2Fproducts%3Fq%3Dmouse');
  });

  it('authGuard na raiz não acrescenta returnUrl', async () => {
    const res = await respondMe(run(authGuard, '/'), 401, {});
    expect(router.serializeUrl(res as UrlTree)).toBe('/login');
  });

  it('authGuard com sessão libera a rota', async () => {
    const res = await respondMe(run(authGuard), 200, { usuario: 'maria', perfil: 'Admin' });
    expect(res).toBe(true);
  });

  it('adminGuard bloqueia Visualizador, avisa e volta ao início', async () => {
    const res = await respondMe(run(adminGuard), 200, { usuario: 'joao', perfil: 'Visualizador' });
    expect(router.serializeUrl(res as UrlTree)).toBe('/');
    expect(snack.open).toHaveBeenCalledTimes(1);
  });

  it('adminGuard libera Admin', async () => {
    const res = await respondMe(run(adminGuard), 200, { usuario: 'maria', perfil: 'Admin' });
    expect(res).toBe(true);
  });

  it('guestGuard: quem já está logado vai ao início; visitante vê o login', async () => {
    const logged = await respondMe(run(guestGuard, '/login'), 200, { usuario: 'maria', perfil: 'Admin' });
    expect(router.serializeUrl(logged as UrlTree)).toBe('/');
  });

  it('guestGuard libera visitante', async () => {
    const res = await respondMe(run(guestGuard, '/login'), 401, {});
    expect(res).toBe(true);
  });

  it('servidor indisponível no boot (500 no /me): as guardas NÃO mandam ao login', async () => {
    const p = run(authGuard);
    await Promise.resolve();
    ctrl.expectOne('/api/auth/me').flush({}, { status: 500, statusText: 'Server Error' });
    expect(await p).toBe(false);
  });

  it('adminGuard sem sessão não avisa "sem permissão" (o login cuida)', async () => {
    const res = await respondMe(run(adminGuard), 401, {});
    expect(router.serializeUrl(res as UrlTree)).toBe('/');
    expect(snack.open).not.toHaveBeenCalled();
  });

  it('o /me do boot é chamado uma única vez para várias guardas', async () => {
    const a = run(authGuard);
    const b = run(adminGuard);
    await Promise.resolve();
    ctrl.expectOne('/api/auth/me').flush({ usuario: 'maria', perfil: 'Admin' });
    expect(await a).toBe(true);
    expect(await b).toBe(true);
    ctrl.verify();
  });
});
