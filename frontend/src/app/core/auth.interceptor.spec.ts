import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { PUBLIC_AUTH } from './http-context';

describe('authInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let auth: { sessionExpired: ReturnType<typeof vi.fn>; refresh: ReturnType<typeof vi.fn> };
  let snack: { open: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    auth = { sessionExpired: vi.fn(), refresh: vi.fn(() => of({ usuario: 'maria', perfil: 'Admin' })) };
    snack = { open: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
        { provide: MatSnackBar, useValue: snack }
      ]
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
  });

  it('401 fora de login/ativar expira a sessão (vai ao /login)', () => {
    let status = 0;
    http.get('/api/products').subscribe({ error: (e) => (status = e.status) });
    ctrl.expectOne('/api/products').flush({ code: 'unauthenticated' }, { status: 401, statusText: 'Unauthorized' });
    expect(auth.sessionExpired).toHaveBeenCalledTimes(1);
    expect(status).toBe(401);
  });

  it('401 em requisição pública (login) é erro normal: não expira a sessão', () => {
    http.post('/api/auth/login', {}, { context: new HttpContext().set(PUBLIC_AUTH, true) }).subscribe({ error: () => {} });
    ctrl.expectOne('/api/auth/login').flush({ code: 'invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });
    expect(auth.sessionExpired).not.toHaveBeenCalled();
  });

  it('403 forbidden avisa e NÃO desloga', () => {
    http.put('/api/products/1', {}).subscribe({ error: () => {} });
    ctrl.expectOne('/api/products/1').flush({ code: 'forbidden' }, { status: 403, statusText: 'Forbidden' });
    expect(snack.open).toHaveBeenCalledTimes(1);
    expect(snack.open.mock.calls[0][0]).toContain('permissão');
    expect(auth.sessionExpired).not.toHaveBeenCalled();
  });

  it('403 csrf_invalid: renova via /me e repete a requisição UMA vez com sucesso', () => {
    let result: unknown;
    http.post('/api/products', { a: 1 }).subscribe((r) => (result = r));

    ctrl.expectOne('/api/products').flush({ code: 'csrf_invalid' }, { status: 403, statusText: 'Forbidden' });
    expect(auth.refresh).toHaveBeenCalledTimes(1);

    ctrl.expectOne('/api/products').flush({ id: 9 }); // o retry
    expect(result).toEqual({ id: 9 });
    expect(auth.sessionExpired).not.toHaveBeenCalled();
    expect(snack.open).not.toHaveBeenCalled();
    ctrl.verify();
  });

  it('o retry de csrf_invalid usa o token NOVO (cookie renovado), não o antigo', () => {
    document.cookie = 'XSRF-TOKEN=velho; path=/';
    auth.refresh.mockImplementation(() => { document.cookie = 'XSRF-TOKEN=novo; path=/'; return of({ usuario: 'maria', perfil: 'Admin' }); });

    http.post('/api/products', {}).subscribe();
    const first = ctrl.expectOne('/api/products');
    expect(first.request.headers.get('X-XSRF-TOKEN')).toBe('velho');
    first.flush({ code: 'csrf_invalid' }, { status: 403, statusText: 'Forbidden' });

    const retry = ctrl.expectOne('/api/products');
    expect(retry.request.headers.get('X-XSRF-TOKEN')).toBe('novo');
    retry.flush({});
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
  });

  it('403 csrf_invalid de novo no retry: devolve o erro, sem laço e sem deslogar', () => {
    let status = 0;
    http.post('/api/products', {}).subscribe({ error: (e) => (status = e.status) });

    ctrl.expectOne('/api/products').flush({ code: 'csrf_invalid' }, { status: 403, statusText: 'Forbidden' });
    ctrl.expectOne('/api/products').flush({ code: 'csrf_invalid' }, { status: 403, statusText: 'Forbidden' });

    expect(status).toBe(403);
    expect(auth.refresh).toHaveBeenCalledTimes(1); // só uma renovação
    expect(auth.sessionExpired).not.toHaveBeenCalled();
    ctrl.verify(); // nenhuma 3ª requisição
  });

  it('csrf_invalid em requisição pública (login) não tenta renovar', () => {
    let status = 0;
    http.post('/api/auth/login', {}, { context: new HttpContext().set(PUBLIC_AUTH, true) }).subscribe({ error: (e) => (status = e.status) });
    ctrl.expectOne('/api/auth/login').flush({ code: 'csrf_invalid' }, { status: 403, statusText: 'Forbidden' });
    expect(status).toBe(403);
    expect(auth.refresh).not.toHaveBeenCalled();
  });

  it('outros erros (500, 429) passam sem efeito colateral', () => {
    http.get('/api/products').subscribe({ error: () => {} });
    ctrl.expectOne('/api/products').flush({}, { status: 500, statusText: 'Server Error' });
    expect(auth.sessionExpired).not.toHaveBeenCalled();
    expect(snack.open).not.toHaveBeenCalled();
  });
});
