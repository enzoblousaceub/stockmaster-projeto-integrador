import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { forkJoin, of, catchError } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { GarmentComponent } from '../../components/garment/garment';
import { ProductService } from '../../services/product.service';
import { AuthService } from '../../core/auth.service';
import { DashboardStats, PAYMENT_LABEL, Sale, StockAlert, monogram, productImageUrl, variantText } from '../../models/product.model';

/** Início do dono: o que fazer agora (atalhos), como foi o dia, o que precisa repor e quanto vale o estoque. */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, MatIconModule, CurrencyPipe, DecimalPipe, DatePipe, GarmentComponent],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss'
})
export class DashboardComponent {
  private api = inject(ProductService);
  readonly auth = inject(AuthService);
  readonly paymentLabel = PAYMENT_LABEL;
  readonly variantText = variantText;
  readonly monogram = monogram;

  readonly state = signal<'loading' | 'error' | 'ready'>('loading');
  readonly stats = signal<DashboardStats | null>(null);
  readonly alerts = signal<StockAlert[]>([]);
  readonly sales = signal<Sale[]>([]);
  readonly showAllAlerts = signal(false);

  readonly name = computed(() => {
    const local = (this.auth.user()?.email ?? '').split('@')[0].split(/[._-]/)[0];
    return local ? local[0].toUpperCase() + local.slice(1) : '';
  });
  readonly greeting = (() => {
    const h = new Date().getHours();
    return h < 12 ? 'Bom dia' : h < 18 ? 'Boa tarde' : 'Boa noite';
  })();
  readonly today = new Date();

  /** vendas de hoje (dia local), sem as desfeitas */
  readonly todaySales = computed(() => {
    const d = new Date().toDateString();
    return this.sales().filter((s) => !s.undoneAt && new Date(s.createdAt).toDateString() === d);
  });
  readonly todayPieces = computed(() => this.todaySales().reduce((n, s) => n + s.totalItems, 0));
  readonly outCount = computed(() => this.alerts().filter((a) => a.quantity === 0).length);
  readonly visibleAlerts = computed(() => (this.showAllAlerts() ? this.alerts() : this.alerts().slice(0, 6)));

  constructor() {
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({
      stats: this.api.getStats(),
      alerts: this.api.getAlerts(),
      // o Visualizador não registra vendas: a lista simplesmente não aparece
      sales: this.auth.canSell() ? this.api.getSales(100).pipe(catchError(() => of([] as Sale[]))) : of([] as Sale[])
    }).subscribe({
      next: ({ stats, alerts, sales }) => {
        this.stats.set(stats);
        this.alerts.set(alerts);
        this.sales.set(sales);
        this.state.set('ready');
      },
      error: () => this.state.set('error')
    });
  }

  saleSub(s: Sale): string {
    const by = s.createdBy && s.createdBy !== 'seed' && s.createdBy !== this.auth.user()?.email ? `, por ${s.createdBy}` : '';
    return this.paymentLabel[s.paymentMethod] + by;
  }

  image(a: StockAlert): string | null {
    return productImageUrl({ id: a.productId, imageUpdatedAt: a.imageUpdatedAt });
  }
}
