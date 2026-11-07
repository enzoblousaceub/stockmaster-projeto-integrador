import { Component, ElementRef, computed, inject, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { ThemeService } from './theme.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatIconModule, MatTooltipModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class AppComponent {
  private router = inject(Router);
  readonly theme = inject(ThemeService);
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

  readonly themeIcon = computed(() => ({ light: 'light_mode', dark: 'dark_mode', system: 'brightness_auto' }[this.theme.mode()]));
  readonly themeLabel = computed(() => ({
    light: 'Tema claro. Alternar para escuro',
    dark: 'Tema escuro. Alternar para automático',
    system: 'Tema automático. Alternar para claro'
  }[this.theme.mode()]));

  skipToContent(event: Event): void {
    event.preventDefault();
    this.main()?.nativeElement.focus();
  }
}
