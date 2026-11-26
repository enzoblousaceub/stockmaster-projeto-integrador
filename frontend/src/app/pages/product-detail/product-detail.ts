import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe, DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin, of, catchError } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ProductService } from '../../services/product.service';
import { AuthService } from '../../core/auth.service';
import {
  MOVEMENT_LABEL, Movement, MovementResult, MovementType, Product, ProductForecast, productCode, productImageUrl
} from '../../models/product.model';
import { MovementDialogComponent, MovementDialogData } from '../../components/movement-dialog/movement-dialog';

/** Ficha do produto: dados, imagem, previsão de ruptura/reposição e histórico de movimentações. */
@Component({
  selector: 'app-product-detail',
  standalone: true,
  imports: [RouterLink, MatIconModule, CurrencyPipe, DatePipe, DecimalPipe, PercentPipe],
  templateUrl: './product-detail.html',
  styleUrl: './product-detail.scss'
})
export class ProductDetailComponent {
  private route = inject(ActivatedRoute);
  private products = inject(ProductService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);
  private auth = inject(AuthService);
  readonly canWrite = this.auth.canWrite;
  readonly canMove = this.auth.canMove;

  readonly state = signal<'loading' | 'error' | 'notfound' | 'ready'>('loading');
  readonly product = signal<Product | null>(null);
  readonly movements = signal<Movement[]>([]);
  readonly forecast = signal<ProductForecast | null>(null);
  readonly label = MOVEMENT_LABEL;
  /** histórico longo: mostra 30 e expande sob demanda */
  readonly shown = signal(30);
  readonly visibleMovements = computed(() => this.movements().slice(0, this.shown()));

  readonly code = computed(() => (this.product() ? productCode(this.product()!.id) : ''));
  readonly image = computed(() => (this.product() ? productImageUrl(this.product()!) : null));
  readonly status = computed<'ok' | 'low' | 'out'>(() => {
    const p = this.product();
    if (!p || p.quantity === 0) return 'out';
    return p.quantity <= p.minStock ? 'low' : 'ok';
  });
  readonly statusLabel = computed(() => ({ ok: 'Em estoque', low: 'Estoque baixo', out: 'Esgotado' })[this.status()]);
  readonly margin = computed(() => {
    const p = this.product();
    return p && p.price > 0 ? (p.price - p.cost) / p.price : null;
  });

  private id = 0;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.id = Number(params.get('id'));
      this.load();
    });
  }

  load(): void {
    this.state.set('loading');
    forkJoin({
      product: this.products.getProduct(this.id),
      movements: this.products.getProductMovements(this.id, 200),
      // a previsão é complementar: se falhar, a ficha abre mesmo assim
      report: this.products.getReport(30).pipe(catchError(() => of(null)))
    }).subscribe({
      next: ({ product, movements, report }) => {
        this.product.set(product);
        this.movements.set(movements);
        this.forecast.set(report?.forecast.find((f) => f.productId === product.id) ?? null);
        this.state.set('ready');
      },
      error: (err) => this.state.set(err?.status === 404 ? 'notfound' : 'error')
    });
  }

  chipClass(type: MovementType): string {
    return type === 'Entrada' ? 'chip chip--in' : type === 'Saida' ? 'chip chip--out' : 'chip chip--adj';
  }

  openMovement(type?: MovementType): void {
    const p = this.product();
    if (!p) return;
    const ref = this.dialog.open<MovementDialogComponent, MovementDialogData, MovementResult>(MovementDialogComponent, {
      width: '460px',
      maxWidth: 'calc(100vw - 32px)',
      ariaLabelledBy: 'movement-title',
      data: { product: p, type }
    });
    ref.afterClosed().subscribe((result) => {
      if (!result) return;
      this.product.set(result.product);
      this.movements.update((list) => [result.movement, ...list]);
      this.snack.open(`Movimentação registrada. Saldo: ${result.product.quantity.toLocaleString('pt-BR')}`, 'Fechar', { duration: 3000 });
      // previsão recalculada com o novo saldo/consumo
      this.products.getReport(30).subscribe({
        next: (r) => this.forecast.set(r.forecast.find((f) => f.productId === result.product.id) ?? null),
        error: () => {}
      });
    });
  }
}
