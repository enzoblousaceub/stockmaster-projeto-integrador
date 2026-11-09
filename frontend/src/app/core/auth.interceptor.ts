import { HttpErrorResponse, HttpInterceptorFn, HttpXsrfTokenExtractor } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { messageFor, parseProblem } from './api-error';
import { CSRF_RETRIED, PUBLIC_AUTH } from './http-context';

/**
 * - 401 fora de login/ativar: sessão acabou -> /login.
 * - 403 csrf_invalid: GET /api/auth/me (renova o token) e repete a requisição UMA vez; não desloga.
 * - 403 forbidden: avisa e NÃO desloga.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse)) return throwError(() => err);
      const problem = parseProblem(err);
      const isPublic = req.context.get(PUBLIC_AUTH);

      if (err.status === 401 && !isPublic) {
        injector.get(AuthService).sessionExpired();
        return throwError(() => err);
      }

      if (err.status === 403) {
        if (problem.code === 'csrf_invalid') {
          if (!isPublic && !req.context.get(CSRF_RETRIED)) {
            return injector.get(AuthService).refresh().pipe(
              switchMap(() => {
                // O clone mantém o X-XSRF-TOKEN antigo: troca pelo cookie que o /me acabou de renovar.
                const token = injector.get(HttpXsrfTokenExtractor).getToken();
                return next(req.clone({
                  headers: token ? req.headers.set('X-XSRF-TOKEN', token) : req.headers.delete('X-XSRF-TOKEN'),
                  context: req.context.set(CSRF_RETRIED, true)
                })).pipe(
                  // sessão revogada entre as duas tentativas
                  catchError((e: unknown) => {
                    if (e instanceof HttpErrorResponse && e.status === 401) injector.get(AuthService).sessionExpired();
                    return throwError(() => e);
                  })
                );
              })
            );
          }
          return throwError(() => err);
        }
        injector.get(MatSnackBar).open(messageFor(problem), 'Fechar', { duration: 4000 });
      }
      return throwError(() => err);
    })
  );
};
