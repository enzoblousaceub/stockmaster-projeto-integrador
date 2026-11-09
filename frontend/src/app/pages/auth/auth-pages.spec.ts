import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LoginComponent } from './login';
import { ActivateComponent } from './activate';
import { UsersComponent } from '../users/users';
import { CreateUserDialogComponent } from '../../components/user-dialogs/user-dialogs';
import { SettingsDialogComponent } from '../../components/settings-dialog/settings-dialog';
import { CodeDialogComponent } from '../../components/code-dialog/code-dialog';
import { AuthService } from '../../core/auth.service';
import { UserService } from '../../services/user.service';
import { ManagedUser } from '../../models/auth.model';

const NO_EMAIL = /e-?mail/i;

/** Todo o texto visível + atributos acessíveis (aria-label, title, placeholder, alt) de um elemento. */
function allText(el: HTMLElement): string {
  const attrs = [...el.querySelectorAll('*')].flatMap((n) =>
    ['aria-label', 'title', 'placeholder', 'alt', 'autocomplete', 'name', 'id'].map((a) => n.getAttribute(a) ?? '')
  );
  return [el.textContent ?? '', ...attrs].join(' ');
}

const me = signal<{ usuario: string; perfil: 'Admin' | 'Visualizador' } | null>({ usuario: 'maria', perfil: 'Admin' });

function authMock() {
  return {
    user: me,
    isAdmin: () => me()?.perfil === 'Admin',
    login: vi.fn(),
    activate: vi.fn(),
    changePassword: vi.fn(),
    logout: vi.fn(() => of(undefined)),
    clear: vi.fn()
  };
}

const users: ManagedUser[] = [
  { id: 1, usuario: 'maria', perfil: 'Admin', status: 'Ativo', bloqueadoAte: null, criadoEm: '2026-01-01T00:00:00Z', criadoPor: null, atualizadoEm: '', atualizadoPor: null, ultimoLoginEm: '2026-11-01T10:00:00Z', codigoExpiraEm: null },
  { id: 2, usuario: 'joao', perfil: 'Visualizador', status: 'Ativo', bloqueadoAte: new Date(Date.now() + 600_000).toISOString(), criadoEm: '', criadoPor: 'maria', atualizadoEm: '', atualizadoPor: null, ultimoLoginEm: null, codigoExpiraEm: null },
  { id: 3, usuario: 'carlos', perfil: 'Visualizador', status: 'Pendente', bloqueadoAte: null, criadoEm: '', criadoPor: 'maria', atualizadoEm: '', atualizadoPor: null, ultimoLoginEm: null, codigoExpiraEm: new Date(Date.now() + 47 * 3_600_000).toISOString() },
  { id: 4, usuario: 'ana', perfil: 'Visualizador', status: 'Inativo', bloqueadoAte: null, criadoEm: '', criadoPor: 'maria', atualizadoEm: '', atualizadoPor: null, ultimoLoginEm: null, codigoExpiraEm: null }
];

describe('telas de autenticação', () => {
  let auth: ReturnType<typeof authMock>;
  let router: Router;
  let query = convertToParamMap({});

  beforeEach(() => {
    vi.useFakeTimers();
    me.set({ usuario: 'maria', perfil: 'Admin' });
    auth = authMock();
    query = convertToParamMap({});
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: UserService, useValue: { list: vi.fn(() => of(users)), create: vi.fn(), patch: vi.fn(), reset: vi.fn(), unlock: vi.fn() } },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
        { provide: MatDialogRef, useValue: { close: vi.fn() } },
        { provide: MAT_DIALOG_DATA, useValue: { title: 'Usuário carlos criado', usuario: 'carlos', code: 'K7QM-2XPD-9HRT', expiresAt: '2026-11-10T10:00:00Z' } },
        { provide: ActivatedRoute, useValue: { get snapshot() { return { queryParamMap: query }; } } }
      ]
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => vi.useRealTimers());

  // ---------- sem e-mail: login por USUÁRIO ----------
  it('login: rótulos pedem USUÁRIO e nada menciona e-mail', () => {
    const f = TestBed.createComponent(LoginComponent);
    f.detectChanges();
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelector('label[for="login-usuario"]')?.textContent?.trim()).toBe('Usuário');
    expect(el.querySelector<HTMLInputElement>('#login-usuario')?.type).toBe('text');
    expect(allText(el)).not.toMatch(NO_EMAIL);
  });

  it('ativar: usuário + código + senha, sem e-mail', () => {
    const f = TestBed.createComponent(ActivateComponent);
    f.detectChanges();
    const el: HTMLElement = f.nativeElement;
    expect(el.querySelector('label[for="ativar-usuario"]')?.textContent?.trim()).toBe('Usuário');
    expect(allText(el)).not.toMatch(NO_EMAIL);
  });

  it('gestão de usuários (todos os status) não menciona e-mail', () => {
    const f = TestBed.createComponent(UsersComponent);
    f.detectChanges();
    expect(allText(f.nativeElement)).not.toMatch(NO_EMAIL);
  });

  it('diálogo Novo usuário: só usuário e perfil (sem nome, sem senha, sem e-mail)', () => {
    const f = TestBed.createComponent(CreateUserDialogComponent);
    f.detectChanges();
    const el: HTMLElement = f.nativeElement;
    expect([...el.querySelectorAll('input')].map((i) => i.id)).toEqual(['new-usuario']);
    expect(el.querySelector('select#new-perfil')).toBeTruthy();
    expect(el.querySelector('input[type="password"]')).toBeNull();
    expect(allText(el)).not.toMatch(NO_EMAIL);
  });

  it('configurações > conta: usuário/perfil/senha/sair, sem e-mail e avisando "todos os dispositivos"', () => {
    const f = TestBed.createComponent(SettingsDialogComponent);
    f.detectChanges();
    const el: HTMLElement = f.nativeElement;
    expect(el.textContent).toContain('maria');
    expect(el.querySelector('#btn-logout')?.textContent).toContain('todos os dispositivos');
    expect(allText(el)).not.toMatch(NO_EMAIL);
  });

  it('diálogo do código: aviso de uso único e sem e-mail', () => {
    const f = TestBed.createComponent(CodeDialogComponent);
    f.detectChanges();
    const el: HTMLElement = f.nativeElement;
    expect(el.textContent).toContain('não será exibido de novo');
    expect(el.querySelector('#activation-code')?.textContent).toContain('K7QM-2XPD-9HRT');
    expect(allText(el)).not.toMatch(NO_EMAIL);
  });

  // ---------- login ----------
  it('login com sucesso volta ao returnUrl seguro', () => {
    query = convertToParamMap({ returnUrl: '/products?q=abc' });
    auth.login.mockReturnValue(of({ usuario: 'maria', perfil: 'Admin' }));
    const f = TestBed.createComponent(LoginComponent);
    f.detectChanges();
    f.componentInstance.usuario.set('maria');
    f.componentInstance.senha.set('segredo123');
    f.componentInstance.submit();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/products?q=abc');
  });

  it('login ignora returnUrl externo (open redirect)', () => {
    query = convertToParamMap({ returnUrl: 'https://evil.com' });
    auth.login.mockReturnValue(of({ usuario: 'maria', perfil: 'Admin' }));
    const f = TestBed.createComponent(LoginComponent);
    f.detectChanges();
    f.componentInstance.usuario.set('maria');
    f.componentInstance.senha.set('x');
    f.componentInstance.submit();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('login inválido mostra mensagem única e limpa a senha', () => {
    auth.login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401, error: { code: 'invalid_credentials' } })));
    const f = TestBed.createComponent(LoginComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.usuario.set('maria'); c.senha.set('errada');
    c.submit();
    expect(c.error()).toBe('Usuário ou senha inválidos.');
    expect(c.senha()).toBe('');
  });

  it('429: bloqueia o envio e faz a contagem regressiva até liberar', () => {
    auth.login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429, error: { code: 'account_locked', retryAfterSeconds: 3 } })));
    const f = TestBed.createComponent(LoginComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.usuario.set('maria'); c.senha.set('x');
    c.submit();
    expect(c.wait()).toBe(3);

    auth.login.mockClear();
    c.submit(); // durante o bloqueio não envia
    expect(auth.login).not.toHaveBeenCalled();

    vi.advanceTimersByTime(3000);
    expect(c.wait()).toBe(0);
    expect(c.error()).toBe('');
  });

  it('login com campos vazios não chama a API', () => {
    const f = TestBed.createComponent(LoginComponent);
    f.detectChanges();
    f.componentInstance.submit();
    expect(auth.login).not.toHaveBeenCalled();
    expect(f.componentInstance.error()).toContain('usuário');
  });

  // ---------- ativar ----------
  it('ativar: senha curta e senhas diferentes são barradas no cliente', () => {
    const f = TestBed.createComponent(ActivateComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.usuario.set('carlos'); c.codigo.set('K7QM-2XPD-9HRT');

    c.novaSenha.set('curta'); c.repetir.set('curta'); c.submit();
    expect(c.passwordError()).toContain('8 e 128');

    c.novaSenha.set('senhalonga1'); c.repetir.set('outra-coisa1'); c.submit();
    expect(c.passwordError()).toContain('não conferem');
    expect(auth.activate).not.toHaveBeenCalled();
  });

  it('ativar com sucesso vai ao login (sem login automático)', () => {
    auth.activate.mockReturnValue(of(undefined));
    const f = TestBed.createComponent(ActivateComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.usuario.set('carlos'); c.codigo.set('K7QM-2XPD-9HRT'); c.novaSenha.set('senhalonga1'); c.repetir.set('senhalonga1');
    c.submit();
    expect(auth.activate).toHaveBeenCalledWith('carlos', 'K7QM-2XPD-9HRT', 'senhalonga1');
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { ativado: 1 } });
  });

  it('ativar: senha fraca fica no campo e o código segue reutilizável', () => {
    auth.activate.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'password_weak' } })));
    const f = TestBed.createComponent(ActivateComponent);
    f.detectChanges();
    const c = f.componentInstance;
    c.usuario.set('carlos'); c.codigo.set('K7QM-2XPD-9HRT'); c.novaSenha.set('12345678'); c.repetir.set('12345678');
    c.submit();
    expect(c.passwordError()).toContain('menos comum');
    expect(c.error()).toBe('');
    expect(c.codigo()).toBe('K7QM-2XPD-9HRT'); // mantido para reenviar
  });

  // ---------- gestão: regras de UI ----------
  it('gestão: o próprio Admin não pode se desativar; Inativo só oferece Reativar; reset de Inativo não aparece', () => {
    const f = TestBed.createComponent(UsersComponent);
    f.detectChanges();
    const el: HTMLElement = f.nativeElement;
    const rows = [...el.querySelectorAll('tbody tr')];
    const ana = rows.find((r) => r.textContent?.includes('ana'))!;
    expect(ana.querySelector('[aria-label="Reativar ana"]')).toBeTruthy();
    expect(ana.querySelector('[aria-label^="Redefinir"]')).toBeNull();
    const maria = rows.find((r) => r.textContent?.includes('maria'))!;
    expect(maria.querySelector('[aria-label^="Desativar"]')).toBeNull();
    expect(maria.querySelector('button[disabled]')).toBeTruthy();
    const joao = rows.find((r) => r.textContent?.includes('joao'))!;
    expect(joao.querySelector('[aria-label="Desbloquear joao"]')).toBeTruthy();
    const carlos = rows.find((r) => r.textContent?.includes('carlos'))!;
    expect(carlos.querySelector('[aria-label="Gerar novo código para carlos"]')).toBeTruthy();
    expect(carlos.textContent).toContain('Pendente');
  });
});
