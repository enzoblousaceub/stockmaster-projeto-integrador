import { Routes } from '@angular/router';
import { DashboardComponent } from './pages/dashboard/dashboard';
import { ProductListComponent } from './pages/product-list/product-list';
import { LoginComponent } from './pages/auth/login';
import { ActivateComponent } from './pages/auth/activate';
import { adminGuard, authGuard, guestGuard, homeGuard, reportsGuard, salesGuard } from './core/guards';

// telas menos usadas carregam sob demanda (bundle inicial menor)
export const routes: Routes = [
  // públicas
  { path: 'login', component: LoginComponent, canActivate: [guestGuard], title: 'Entrar · Stock Master' },
  { path: 'ativar', component: ActivateComponent, canActivate: [guestGuard], title: 'Ativar acesso · Stock Master' },

  // autenticadas
  { path: '', component: DashboardComponent, canActivate: [authGuard, homeGuard], title: 'Início · Stock Master' },
  // venda no balcão: Admin e Operador
  { path: 'vender', loadComponent: () => import('./pages/sale/sale').then((m) => m.SaleComponent), canActivate: [authGuard, salesGuard], title: 'Vender · Stock Master' },
  { path: 'products', component: ProductListComponent, canActivate: [authGuard], title: 'Produtos · Stock Master' },
  // escrita em produtos: só Admin
  { path: 'products/new', loadComponent: () => import('./pages/product-form/product-form').then((m) => m.ProductFormComponent), canActivate: [authGuard, adminGuard], title: 'Novo produto · Stock Master' },
  { path: 'products/:id/edit', loadComponent: () => import('./pages/product-form/product-form').then((m) => m.ProductFormComponent), canActivate: [authGuard, adminGuard], title: 'Editar produto · Stock Master' },
  { path: 'products/:id', loadComponent: () => import('./pages/product-detail/product-detail').then((m) => m.ProductDetailComponent), canActivate: [authGuard], title: 'Produto · Stock Master' },
  { path: 'reports', loadComponent: () => import('./pages/reports/reports').then((m) => m.ReportsComponent), canActivate: [authGuard, reportsGuard], title: 'Relatórios · Stock Master' },
  // gestão de usuários: só Admin
  { path: 'users', loadComponent: () => import('./pages/users/users').then((m) => m.UsersComponent), canActivate: [authGuard, adminGuard], title: 'Equipe · Stock Master' },

  { path: '**', redirectTo: '' }
];
