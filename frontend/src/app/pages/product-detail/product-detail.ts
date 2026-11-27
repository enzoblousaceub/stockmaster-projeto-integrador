import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe, DecimalPipe, formatDate } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { concatMap, forkJoin, from, of, catchError, map, toArray } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { GarmentComponent } from '../../components/garment/garment';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ProductService } from '../../services/product.service';
import { AuthService } from '../../core/auth.service';
import { DeleteDialogComponent } from '../../components/delete-dialog/delete-dialog';
import {
  MOVEMENT_LABEL, Movement, MovementType, PAYMENT_LABEL, Product, ProductForecast, Variant, isSingle, levelOf, materialsOf,
  monogram, productImageUrl, variantText
} from '../../models/product.model';

/** Ficha do produto: foto, grade de tamanhos com o saldo de cada um, entrada de mercadoria e histórico. */
@Component({
  selector: 'app-product-detail',
  standalone: true,
  imports: [RouterLink, MatIconModule, CurrencyPipe, DecimalPipe, GarmentComponent],
  templateUrl: './product-detail.html',
  styleUrl: './product-detail.scss'
})
export class ProductDetailComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private api = inject(ProductService);
  private dialog = inject(MatDialog);
  private snack = inject(MatSnackBar);
  private destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly label = MOVEMENT_LABEL;
  readonly paymentLabel = PAYMENT_LABEL;
  readonly variantText = variantText;
  readonly levelOf = levelOf;
  readonly monogram = monogram;

  readonly state = signal<'loading' | 'error' | 'notfound' | 'ready'>('loading');
  readonly product = signal<Product | null>(null);
  readonly movements = signal<Movement[]>([]);
  readonly forecast = signal<ProductForecast | null>(null);
  readonly shown = signal(20);
  readonly visibleMovements = computed(() => this.movements().slice(0, this.shown()));

  readonly image = computed(() => (this.product() ? productImageUrl(this.product()!) : null));
  readonly single = computed(() => (this.product() ? isSingle(this.product()!) : false));
  /** grade agrupada por material, na ordem do cadastro */
  readonly groups = computed(() => {
    const p = this.product();
    if (!p) return [];
    return materialsOf(p).map((m) => ({ material: m, variants: p.variants.filter((v) => v.material === m) }));
  });
  readonly margin = computed(() => {
    const p = this.product();
    return p && p.price > 0 ? Math.round(((p.price - p.cost) / p.price) * 100) : null;
  });

  // ---------- chegou mercadoria ----------
  readonly entryOpen = signal(false);
  readonly entry = signal<Record<number, number>>({});
  readonly entryTotal = computed(() => Object.values(this.entry()).reduce((s, n) => s + (n || 0), 0));
  readonly saving = signal(false);

  private id = 0;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.id = Number(params.get('id'));
      this.load(this.route.snapshot.queryParamMap.get('entrada') === '1');
    });
  }

  load(openEntry = false): void {
    this.state.set('loading');
    const canReport = this.auth.canReport();
    forkJoin({
      product: this.api.getProduct(this.id),
      // histórico e previsão: só Admin/Visualizador; se falharem, a ficha abre mesmo assim
      movements: canReport ? this.api.getProductMovements(this.id, 200).pipe(catchError(() => of([] as Movement[]))) : of([] as Movement[]),
      report: canReport ? this.api.getReport(30).pipe(catchError(() => of(null))) : of(null)
    }).subscribe({
      next: ({ product, movements, report }) => {
        this.product.set(product);
        this.movements.set(movements);
        this.forecast.set(report?.forecast.find((f) => f.productId === product.id) ?? null);
        this.state.set('ready');
        if (openEntry && this.auth.canMove()) this.openEntry();
      },
      error: (err) => this.state.set(err?.status === 404 ? 'notfound' : 'error')
    });
  }

  sizeText(v: Variant): string {
    if (v.size === 'Único' && !v.material) return 'Tamanho único';
    return v.material ? `${v.size} ${v.material}` : v.size;
  }

  /** "26/11/26 14:13, Pix, Chegou mercadoria, joana@loja.com" */
  moveMeta(m: Movement): string {
    const parts = [formatDate(m.occurredAt, 'dd/MM/yy HH:mm', 'pt-BR')];
    if (m.paymentMethod) parts.push(this.paymentLabel[m.paymentMethod]);
    if (m.reason && m.reason !== 'Venda') parts.push(m.reason);
    if (m.createdBy && m.createdBy !== 'seed') parts.push(m.createdBy);
    return parts.join(', ');
  }

  chipClass(type: MovementType): string {
    return type === 'Entrada' ? 'chip chip--in' : type === 'Saida' ? 'chip chip--out' : 'chip chip--adj';
  }

  // ---------- entrada ----------
  openEntry(): void {
    this.entry.set({});
    this.entryOpen.set(true);
  }

  closeEntry(): void {
    this.entryOpen.set(false);
    if (this.route.snapshot.queryParamMap.has('entrada'))
      this.router.navigate([], { queryParams: { entrada: null }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  setEntry(v: Variant, value: number): void {
    const n = Math.max(0, Math.min(10000, Math.floor(Number(value) || 0)));
    this.entry.update((e) => ({ ...e, [v.id]: n }));
  }

  stepEntry(v: Variant, d: number): void {
    this.setEntry(v, (this.entry()[v.id] ?? 0) + d);
  }

  /** Uma entrada por tamanho, em sequência. Se uma falhar, as que já entraram ficam e a tela diz quais faltaram. */
  saveEntry(): void {
    const p = this.product();
    const rows = p?.variants.filter((v) => (this.entry()[v.id] ?? 0) > 0) ?? [];
    if (!p || rows.length === 0 || this.saving()) return;
    this.saving.set(true);
    from(rows).pipe(
      concatMap((v) => this.api.addMovement(p.id, { type: 'Entrada', variantId: v.id, quantity: this.entry()[v.id], reason: 'Chegou mercadoria' })
        .pipe(map(() => ({ v, ok: true })), catchError(() => of({ v, ok: false })))),
      toArray()
    ).subscribe((results) => {
      this.saving.set(false);
      const failed = results.filter((r) => !r.ok);
      const added = results.filter((r) => r.ok).reduce((s, r) => s + this.entry()[r.v.id], 0);
      if (failed.length === 0) {
        this.entryOpen.set(false);
        this.snack.open(`${added} ${added === 1 ? 'peça somada' : 'peças somadas'} ao estoque.`, 'Fechar', { duration: 4000 });
        this.closeEntry();
      } else {
        // mantém na tela só o que não entrou, para tentar de novo
        this.entry.set(Object.fromEntries(failed.map((r) => [r.v.id, this.entry()[r.v.id]])));
        this.snack.open(`${failed.length === results.length ? 'Nada foi somado' : 'Parte entrou'}. Os tamanhos que ficaram na tela não foram somados; tente de novo.`, 'Fechar', { duration: 7000 });
      }
      this.reload();
    });
  }

  private reload(): void {
    forkJoin({
      product: this.api.getProduct(this.id),
      movements: this.auth.canReport() ? this.api.getProductMovements(this.id, 200).pipe(catchError(() => of(this.movements()))) : of([] as Movement[])
    }).subscribe({
      next: ({ product, movements }) => { this.product.set(product); this.movements.set(movements); },
      error: () => {}
    });
  }

  // ---------- excluir ----------
  confirmDelete(): void {
    const p = this.product();
    if (!p) return;
    this.dialog.open(DeleteDialogComponent, { width: '440px', maxWidth: 'calc(100vw - 32px)', data: { name: p.name }, ariaLabelledBy: 'delete-title' })
      .afterClosed().subscribe((ok) => {
        if (!ok) return;
        this.api.deleteProduct(p.id).subscribe({
          next: () => {
            this.snack.open('Produto excluído. O histórico de vendas continua nos relatórios.', 'Fechar', { duration: 4000 });
            this.router.navigate(['/products']);
          },
          error: () => this.snack.open('Não deu para excluir agora. Tente de novo.', 'Fechar', { duration: 4000 })
        });
      });
  }
}
