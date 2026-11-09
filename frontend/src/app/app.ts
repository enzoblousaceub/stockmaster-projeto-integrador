import { Component, ElementRef, computed, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { SettingsDialogComponent } from './components/settings-dialog/settings-dialog';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatIconModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class AppComponent {
  private router = inject(Router);
  private dialog = inject(MatDialog);
  readonly auth = inject(AuthService);

  constructor() {
    this.auth.ensureLoaded(); // descobre a sessão no boot (GET /api/auth/me)
  }

  readonly initials = computed(() => (this.auth.user()?.usuario ?? '').slice(0, 2).toUpperCase());

  /** Encerra a sessão em TODOS os dispositivos; mesmo se falhar, limpa o estado local. */
  logout(): void {
    const done = () => { this.auth.clear(); this.router.navigate(['/login']); };
    this.auth.logout().subscribe({ next: done, error: done });
  }
  private main = viewChild<ElementRef<HTMLElement>>('main');

  private url = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url }
  );

  readonly query = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.currentQuery())),
    { initialValue: this.currentQuery() }
  );

  /** ?q= como texto único (se vier repetido, usa o primeiro). */
  private currentQuery(): string {
    const q = this.router.parseUrl(this.router.url).queryParams['q'];
    return String(Array.isArray(q) ? q[0] : q ?? '');
  }

  onSearch(event: Event, value: string): void {
    event.preventDefault();
    const q = value.trim();
    this.router.navigate(['/products'], { queryParams: { q: q || null } });
  }

  /** "Produtos" fica ativo na lista e na edição, mas não em /products/new (que tem item próprio). */
  readonly productsActive = computed(() => {
    const path = this.url().split(/[?#]/)[0];
    return path.startsWith('/products') && path !== '/products/new';
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
