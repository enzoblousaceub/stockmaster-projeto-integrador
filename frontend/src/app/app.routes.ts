import { Routes } from '@angular/router';
import { DashboardComponent } from './pages/dashboard/dashboard';
import { ProductListComponent } from './pages/product-list/product-list';
import { LoginComponent } from './pages/auth/login';
import { ActivateComponent } from './pages/auth/activate';
import { adminGuard, authGuard, guestGuard } from './core/guards';

// telas menos usadas carregam sob demanda (bundle inicial menor)
export const routes: Routes = [
  // públicas
  { path: 'login', component: LoginComponent, canActivate: [guestGuard], title: 'Entrar · Stock Master' },
  { path: 'ativar', component: ActivateComponent, canActivate: [guestGuard], title: 'Ativar acesso · Stock Master' },

  // autenticadas
  { path: '', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'products', component: ProductListComponent, canActivate: [authGuard] },
  // escrita em produtos: só Admin
  { path: 'products/new', loadComponent: () => import('./pages/product-form/product-form').then((m) => m.ProductFormComponent), canActivate: [authGuard, adminGuard] },
  { path: 'products/:id/edit', loadComponent: () => import('./pages/product-form/product-form').then((m) => m.ProductFormComponent), canActivate: [authGuard, adminGuard] },
  // ficha do produto com histórico e previsão: qualquer perfil
  { path: 'products/:id', loadComponent: () => import('./pages/product-detail/product-detail').then((m) => m.ProductDetailComponent), canActivate: [authGuard], title: 'Produto · Stock Master' },
  { path: 'reports', loadComponent: () => import('./pages/reports/reports').then((m) => m.ReportsComponent), canActivate: [authGuard], title: 'Relatórios · Stock Master' },
  // gestão de usuários: só Admin
  { path: 'users', loadComponent: () => import('./pages/users/users').then((m) => m.UsersComponent), canActivate: [authGuard, adminGuard], title: 'Usuários · Stock Master' },

  { path: '**', redirectTo: '' }
];
