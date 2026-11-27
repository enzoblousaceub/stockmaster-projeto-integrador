import { Component, ElementRef, computed, inject, viewChild } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { SettingsDialogComponent } from './components/settings-dialog/settings-dialog';
import { AuthService } from './core/auth.service';

interface NavLink { path: string; label: string; icon: string; id: string; exact: boolean; }

const ROLE_NAME: Record<string, string> = { Admin: 'Dono', Operador: 'Vendedor', Visualizador: 'Consulta' };

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatIconModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class AppComponent {
  private dialog = inject(MatDialog);
  readonly auth = inject(AuthService);
  private main = viewChild<ElementRef<HTMLElement>>('main');

  constructor() {
    this.auth.ensureLoaded(); // descobre a sessão no boot (GET /api/auth/me)
  }

  readonly initials = computed(() => (this.auth.user()?.email ?? '').slice(0, 2).toUpperCase());
  /** "admin@loja.com" vira "Admin": nome curto para o topo. */
  readonly firstName = computed(() => {
    const local = (this.auth.user()?.email ?? '').split('@')[0].split(/[._-]/)[0];
    return local ? local[0].toUpperCase() + local.slice(1) : '';
  });
  readonly roleName = computed(() => ROLE_NAME[this.auth.user()?.perfil ?? ''] ?? '');

  /** Menu por perfil: o vendedor só vê o que usa no balcão. */
  readonly links = computed<NavLink[]>(() => {
    const a = this.auth;
    const all: (NavLink & { show: boolean })[] = [
      { path: '/', label: 'Início', icon: 'home', id: 'home', exact: true, show: !a.isOperator() },
      { path: '/vender', label: 'Vender', icon: 'shopping_bag', id: 'sell', exact: false, show: a.canSell() },
      { path: '/products', label: 'Produtos', icon: 'checkroom', id: 'products', exact: false, show: true },
      { path: '/reports', label: 'Relatórios', icon: 'insights', id: 'reports', exact: false, show: a.canReport() },
      { path: '/users', label: 'Equipe', icon: 'group', id: 'users', exact: false, show: a.isAdmin() }
    ];
    return all.filter((l) => l.show);
  });

  openSettings(): void {
    this.dialog.open(SettingsDialogComponent, {
      width: '440px',
      maxWidth: 'calc(100vw - 32px)',
      ariaLabelledBy: 'settings-title',
      autoFocus: '[cdkFocusInitial]',
      restoreFocus: true
    });
  }

  skipToContent(event: Event): void {
    event.preventDefault();
    this.main()?.nativeElement.focus();
  }
}
