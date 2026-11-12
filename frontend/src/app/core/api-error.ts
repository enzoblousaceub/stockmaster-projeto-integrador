import { HttpErrorResponse } from '@angular/common/http';
import { ApiProblem } from '../models/auth.model';

/** Extrai o ProblemDetails de qualquer erro HTTP (status 0 = sem rede). */
export function parseProblem(err: unknown): ApiProblem {
  if (!(err instanceof HttpErrorResponse)) return { status: -1, code: 'unknown' };
  const body = typeof err.error === 'object' && err.error !== null ? (err.error as Partial<ApiProblem>) : {};
  const header = Number(err.headers?.get('Retry-After'));
  return {
    status: err.status,
    code: body.code ?? (err.status === 0 ? 'network' : undefined),
    title: body.title,
    detail: body.detail,
    errors: body.errors,
    retryAfterSeconds: Number.isFinite(body.retryAfterSeconds) ? body.retryAfterSeconds : (header > 0 ? header : undefined)
  };
}

/** "4 min 30 s", "45 s" */
export function formatWait(seconds: number): string {
  const s = Math.max(0, Math.ceil(seconds));
  const m = Math.floor(s / 60);
  return m > 0 ? `${m} min${s % 60 ? ` ${s % 60} s` : ''}` : `${s} s`;
}

/** Texto em pt-BR por `code` (os textos do servidor podem mudar; o code não). */
export function messageFor(p: ApiProblem): string {
  const wait = p.retryAfterSeconds ? ` Tente novamente em ${formatWait(p.retryAfterSeconds)}.` : '';
  switch (p.code) {
    case 'invalid_credentials': return 'E-mail ou senha inválidos.';
    case 'account_locked': return `Muitas tentativas seguidas; o acesso foi bloqueado temporariamente.${wait}`;
    case 'rate_limited': return `Muitas tentativas em pouco tempo.${wait || ' Aguarde um instante.'}`;
    case 'validation_error': {
      const first = p.errors ? Object.values(p.errors).flat()[0] : undefined;
      return first ?? 'Verifique os campos informados.';
    }
    case 'current_password_invalid': return 'A senha atual está incorreta.';
    case 'password_too_short': return 'A senha deve ter entre 8 e 128 caracteres.';
    case 'password_weak': return 'Escolha uma senha menos comum: não use o próprio e-mail nem caracteres repetidos.';
    case 'activation_code_invalid': return 'Código de ativação inválido. Confira o e-mail e o código.';
    case 'activation_code_expired': return 'Este código expirou. Peça um novo código ao administrador.';
    case 'activation_code_used': return 'Este código já foi utilizado. Entre com a sua senha ou peça um novo código ao administrador.';
    case 'email_exists': return 'Já existe um usuário com este e-mail.';
    case 'email_locked': return 'O e-mail só pode ser alterado enquanto a pessoa não ativou o acesso.';
    case 'user_inactive': return 'Usuário inativo. Reative-o antes de redefinir o acesso.';
    case 'last_admin': return 'Não é possível desativar, rebaixar ou redefinir o último administrador ativo.';
    case 'forbidden': return 'Você não tem permissão para esta ação.';
    case 'csrf_invalid': return 'A verificação de segurança falhou. Recarregue a página e tente novamente.';
    case 'service_misconfigured': return 'O serviço de ativação está indisponível. Avise o responsável pelo sistema.';
    case 'unauthenticated': return 'Sua sessão expirou. Entre novamente.';
    case 'network': return 'Sem conexão com o servidor. Verifique sua internet e tente novamente.';
    default: return 'Não foi possível concluir a operação. Tente novamente.';
  }
}

/** Códigos conhecidos (para testes). */
export const KNOWN_CODES = [
  'invalid_credentials', 'account_locked', 'rate_limited', 'validation_error', 'current_password_invalid',
  'password_too_short', 'password_weak', 'activation_code_invalid', 'activation_code_expired',
  'activation_code_used', 'email_exists', 'email_locked', 'user_inactive', 'last_admin', 'forbidden', 'csrf_invalid',
  'service_misconfigured', 'unauthenticated', 'network'
] as const;

/** Só devolve caminhos internos (evita open redirect via returnUrl). */
export function safeReturnUrl(url: string | null | undefined): string {
  if (!url || url.length > 512 || /[\\\r\n\t]/.test(url) || !url.startsWith('/') || url.startsWith('//')) return '/';
  if (url.startsWith('/login') || url.startsWith('/ativar')) return '/';
  return url;
}
