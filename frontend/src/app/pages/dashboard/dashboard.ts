import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { ProductService } from '../../services/product.service';
import { DashboardStats, Product } from '../../models/product.model';

interface CategoryBar {
  name: string;
  items: number;
  value: number;
  products: number;
  percent: number;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, MatIconModule, CurrencyPipe, DecimalPipe],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss'
})
export class DashboardComponent {
  private productService = inject(ProductService);

  readonly state = signal<'loading' | 'error' | 'ready'>('loading');
  readonly stats = signal<DashboardStats | null>(null);
  readonly products = signal<Product[]>([]);

  /** Itens em ou abaixo do mínimo, os mais críticos primeiro (esgotados, depois menor razão qtd/mín). */
  readonly alerts = computed(() =>
    this.products()
      .filter((p) => p.quantity <= p.minStock)
      .sort((a, b) => this.severity(a) - this.severity(b) || a.name.localeCompare(b.name))
  );
  readonly topAlerts = computed(() => this.alerts().slice(0, 6));
  readonly alertCount = computed(() => {
    const s = this.stats();
    return s ? s.lowStockCount + s.outOfStockCount : 0;
  });

  readonly categories = computed<CategoryBar[]>(() => {
    const map = new Map<string, CategoryBar>();
    for (const p of this.products()) {
      const c = map.get(p.category) ?? { name: p.category, items: 0, value: 0, products: 0, percent: 0 };
      c.items += p.quantity;
      c.value += p.quantity * p.price;
      c.products += 1;
      map.set(p.category, c);
    }
    const list = [...map.values()].sort((a, b) => b.items - a.items);
    const max = Math.max(1, ...list.map((c) => c.items));
    return list.map((c) => ({ ...c, percent: Math.round((c.items / max) * 100) }));
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({ stats: this.productService.getStats(), products: this.productService.getProducts() }).subscribe({
      next: ({ stats, products }) => {
        this.stats.set(stats);
        this.products.set(products);
        this.state.set('ready');
      },
      error: () => this.state.set('error')
    });
  }

  isOut(p: Product): boolean {
    return p.quantity === 0;
  }

  private severity(p: Product): number {
    return p.quantity === 0 ? -1 : p.quantity / Math.max(p.minStock, 1);
  }
}
