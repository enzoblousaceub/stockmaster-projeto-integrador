import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { KNOWN_CODES, formatWait, messageFor, parseProblem, safeReturnUrl } from './api-error';

describe('parseProblem', () => {
  it('lê code, errors e retryAfterSeconds do corpo', () => {
    const p = parseProblem(new HttpErrorResponse({
      status: 429, error: { code: 'account_locked', retryAfterSeconds: 42, title: 'Muitas tentativas' }
    }));
    expect(p).toMatchObject({ status: 429, code: 'account_locked', retryAfterSeconds: 42 });
  });

  it('usa o header Retry-After quando o corpo não traz', () => {
    const p = parseProblem(new HttpErrorResponse({ status: 429, error: { code: 'rate_limited' }, headers: new HttpHeaders({ 'Retry-After': '30' }) }));
    expect(p.retryAfterSeconds).toBe(30);
  });

  it('status 0 vira code network', () => {
    expect(parseProblem(new HttpErrorResponse({ status: 0 })).code).toBe('network');
  });

  it('erro que não é HTTP vira unknown', () => {
    expect(parseProblem(new Error('x')).code).toBe('unknown');
  });
});

describe('messageFor', () => {
  it('tem mensagem em pt-BR para todos os codes conhecidos', () => {
    for (const code of KNOWN_CODES) {
      const msg = messageFor({ status: 400, code });
      expect(msg.length, code).toBeGreaterThan(5);
      expect(msg, code).not.toBe(messageFor({ status: 400, code: 'inexistente' }));
    }
  });

  it('nunca menciona e-mail (o login é por usuário)', () => {
    for (const code of [...KNOWN_CODES, 'inexistente']) {
      expect(messageFor({ status: 400, code }), code).not.toMatch(/e-?mail/i);
    }
  });

  it('login inválido não revela se o usuário existe', () => {
    expect(messageFor({ status: 401, code: 'invalid_credentials' })).toBe('Usuário ou senha inválidos.');
  });

  it('429 inclui o tempo de espera quando conhecido', () => {
    expect(messageFor({ status: 429, code: 'account_locked', retryAfterSeconds: 90 })).toContain('1 min 30 s');
  });

  it('validation_error mostra a primeira mensagem do campo', () => {
    expect(messageFor({ status: 400, code: 'validation_error', errors: { usuario: ['Usuário inválido.'] } })).toBe('Usuário inválido.');
  });
});

describe('formatWait', () => {
  it.each([[45, '45 s'], [60, '1 min'], [90, '1 min 30 s'], [0, '0 s']])('%s s -> %s', (s, txt) => {
    expect(formatWait(s)).toBe(txt);
  });
});

describe('safeReturnUrl', () => {
  it('aceita só caminhos internos', () => {
    expect(safeReturnUrl('/products?q=a')).toBe('/products?q=a');
    expect(safeReturnUrl('https://evil.com')).toBe('/');
    expect(safeReturnUrl('//evil.com')).toBe('/');
    expect(safeReturnUrl('/\\evil.com')).toBe('/');
    expect(safeReturnUrl(null)).toBe('/');
  });

  it('não volta para login/ativar', () => {
    expect(safeReturnUrl('/login?x=1')).toBe('/');
    expect(safeReturnUrl('/ativar')).toBe('/');
  });
});
