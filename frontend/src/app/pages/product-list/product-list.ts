import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { GarmentComponent } from '../../components/garment/garment';
import { ProductService } from '../../services/product.service';
import { AuthService } from '../../core/auth.service';
import { Product, isSingle, levelOf, monogram, productImageUrl } from '../../models/product.model';

/**
 * Lista de produtos (todos os perfis). Com ?modo=entrada vira o primeiro passo de "Chegou mercadoria":
 * tocar no produto abre a ficha já na entrada de peças.
 */
@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [RouterLink, MatIconModule, GarmentComponent],
  templateUrl: './product-list.html',
  styleUrl: './product-list.scss'
})
export class ProductListComponent {
  private api = inject(ProductService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  readonly auth = inject(AuthService);
  readonly canWrite = this.auth.canWrite;
  readonly monogram = monogram;
  readonly imageOf = productImageUrl;

  readonly state = signal<'loading' | 'error' | 'ready'>('loading');
  readonly products = signal<Product[]>([]);
  readonly search = signal('');
  readonly category = signal('');
  readonly arrival = signal(false);

  readonly categories = computed(() => [...new Set(this.products().map((p) => p.category))].sort((a, b) => a.localeCompare(b, 'pt-BR')));
  readonly visible = computed(() => {
    const q = this.norm(this.search());
    const c = this.category();
    return this.products()
      .filter((p) => !c || p.category === c)
      .filter((p) => !q || this.norm(`${p.name} ${p.brand ?? ''} ${p.category} ${p.description ?? ''}`).includes(q))
      .sort((a, b) => a.name.localeCompare(b.name, 'pt-BR'));
  });

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((q) => {
      this.search.set(q.get('q') ?? '');
      this.arrival.set(q.get('modo') === 'entrada' && this.auth.canMove());
    });
    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.api.getProducts().subscribe({
      next: (list) => { this.products.set(list); this.state.set('ready'); },
      error: () => this.state.set('error')
    });
  }

  private norm(s: string): string {
    return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
  }

  onSearch(value: string): void {
    this.search.set(value);
  }

  leaveArrival(): void {
    this.router.navigate([], { queryParams: { modo: null }, queryParamsHandling: 'merge' });
  }

  /** Resumo da grade para a linha: "38 a 46, Jeans e Sarja" ou "Tamanho único". */
  gridText(p: Product): string {
    if (isSingle(p)) return 'Tamanho único';
    const sizes = [...new Set(p.variants.map((v) => v.size))];
    const mats = [...new Set(p.variants.map((v) => v.material).filter(Boolean))];
    const sizeText = sizes.length > 3 ? `${sizes[0]} ao ${sizes[sizes.length - 1]}` : sizes.join(', ');
    return mats.length ? `${sizeText}, ${mats.join(' e ')}` : sizeText;
  }

  /** Tamanhos esgotados/no mínimo, para o selo da linha. */
  alert(p: Product): { out: number; low: number } {
    let out = 0, low = 0;
    for (const v of p.variants) {
      const l = levelOf(v);
      if (l === 'out') out++;
      else if (l === 'low') low++;
    }
    return { out, low };
  }
}
