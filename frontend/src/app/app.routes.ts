import { Routes } from '@angular/router';
import { DashboardComponent } from './pages/dashboard/dashboard';
import { ProductListComponent } from './pages/product-list/product-list';
import { ProductFormComponent } from './pages/product-form/product-form';
import { LoginComponent } from './pages/auth/login';
import { ActivateComponent } from './pages/auth/activate';
import { UsersComponent } from './pages/users/users';
import { adminGuard, authGuard, guestGuard } from './core/guards';

export const routes: Routes = [
  // públicas
  { path: 'login', component: LoginComponent, canActivate: [guestGuard], title: 'Entrar · Stock Master' },
  { path: 'ativar', component: ActivateComponent, canActivate: [guestGuard], title: 'Ativar acesso · Stock Master' },

  // autenticadas
  { path: '', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'products', component: ProductListComponent, canActivate: [authGuard] },
  // escrita em produtos: só Admin
  { path: 'products/new', component: ProductFormComponent, canActivate: [authGuard, adminGuard] },
  { path: 'products/:id/edit', component: ProductFormComponent, canActivate: [authGuard, adminGuard] },
  // gestão de usuários: só Admin
  { path: 'users', component: UsersComponent, canActivate: [authGuard, adminGuard], title: 'Usuários · Stock Master' },

  { path: '**', redirectTo: '' }
];
