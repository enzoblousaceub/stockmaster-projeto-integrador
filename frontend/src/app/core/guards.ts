import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from './auth.service';

/** Exige sessão; senão vai ao /login guardando o destino. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.ensureLoaded();
  if (!auth.ready()) return false; // servidor indisponível: não decide (o app mostra "tentar novamente")
  if (auth.isLoggedIn()) return true;
  return router.createUrlTree(['/login'], { queryParams: state.url !== '/' ? { returnUrl: state.url } : {} });
};

/** Só Admin (o backend também exige). */
export const adminGuard: CanActivateFn = async () => {
  // inject() só funciona de forma síncrona: tudo antes do primeiro await
  const auth = inject(AuthService);
  const router = inject(Router);
  const snack = inject(MatSnackBar);
  await auth.ensureLoaded();
  if (!auth.ready()) return false;
  if (auth.isAdmin()) return true;
  // sem sessão o authGuard (em paralelo) já redireciona ao login: não avisa "sem permissão" a quem nem entrou
  if (!auth.isLoggedIn()) return router.createUrlTree(['/']);
  snack.open('Você não tem permissão para acessar esta página.', 'Fechar', { duration: 4000 });
  return router.createUrlTree(['/']);
};

/** Telas públicas (login/ativar): quem já está logado vai para o início. */
export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  await auth.ensureLoaded();
  if (!auth.ready()) return false;
  return auth.isLoggedIn() ? router.createUrlTree(['/']) : true;
};
