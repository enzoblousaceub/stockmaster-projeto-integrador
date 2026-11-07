import { Component, ElementRef, computed, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { SettingsDialogComponent } from './components/settings-dialog/settings-dialog';

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
  private main = viewChild<ElementRef<HTMLElement>>('main');

  private url = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url }
  );

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
      autoFocus: 'first-tabbable',
      restoreFocus: true
    });
  }

  skipToContent(event: Event): void {
    event.preventDefault();
    this.main()?.nativeElement.focus();
  }
}
