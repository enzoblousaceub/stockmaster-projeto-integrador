import { HttpContextToken } from '@angular/common/http';

/** Requisição "pública": um 401 aqui é erro normal (login, ativar, /me no boot), não expira a sessão. */
export const PUBLIC_AUTH = new HttpContextToken<boolean>(() => false);

/** Marca que o retry por csrf_invalid já foi feito (no máximo 1 vez). */
export const CSRF_RETRIED = new HttpContextToken<boolean>(() => false);

/** Um 401 nesta requisição não expira a sessão (ex.: logout). O retry de csrf_invalid continua valendo. */
export const NO_401_REDIRECT = new HttpContextToken<boolean>(() => false);
