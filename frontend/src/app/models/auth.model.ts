export type Perfil = 'Admin' | 'Visualizador';
export type UserStatus = 'Pendente' | 'Ativo' | 'Inativo';

/** Resposta de /api/auth/login e /api/auth/me */
export interface Me {
  email: string;
  perfil: Perfil;
}

/** Usuário na gestão (somente Admin) */
export interface ManagedUser {
  id: number;
  email: string;
  perfil: Perfil;
  status: UserStatus;
  bloqueadoAte: string | null;
  criadoEm: string;
  criadoPor: string | null;
  atualizadoEm: string;
  atualizadoPor: string | null;
  ultimoLoginEm: string | null;
  codigoExpiraEm: string | null;
}

/** Código de ativação: exibido UMA vez */
export interface ActivationCode {
  /** ausente quando o convite foi enviado por e-mail (só o destinatário conhece o código) */
  codigoAtivacao?: string | null;
  codigoExpiraEm: string;
  /** true = e-mail enviado; false = falhou (o código vem junto) */
  emailEnviado?: boolean | null;
}

export type CreatedUser = ManagedUser & ActivationCode;
export type PatchedUser = ManagedUser & Partial<ActivationCode>;

/** ProblemDetails do backend (RFC 7807) + code estável */
export interface ApiProblem {
  status: number;
  code?: string;
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
  retryAfterSeconds?: number;
}
