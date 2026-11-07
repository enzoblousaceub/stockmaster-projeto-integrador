import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { MatPaginatorIntl, MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Injectable } from '@angular/core';
import { ProductService } from '../../services/product.service';
import { Product } from '../../models/product.model';
import { DeleteDialogComponent } from '../../components/delete-dialog/delete-dialog';

@Injectable()
class PtPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'Itens por página';
  override nextPageLabel = 'Próxima página';
  override previousPageLabel = 'Página anterior';
  override firstPageLabel = 'Primeira página';
  override lastPageLabel = 'Última página';
  override getRangeLabel = (page: number, size: number, length: number) => {
    if (length === 0) return '0 de 0';
    const start = page * size + 1;
    return `${start}–${Math.min(length, (page + 1) * size)} de ${length}`;
  };
}

type SortKey = 'name' | 'category' | 'quantity' | 'price' | 'status';
type Status = 'out' | 'low' | 'ok';

const MAX_QTY = 1_000_000;

@Component({
  selector: 'app-product-list',
  standalone: true,
  imports: [RouterLink, MatIconModule, MatPaginatorModule, CurrencyPipe, DecimalPipe],
  providers: [{ provide: MatPaginatorIntl, useClass: PtPaginatorIntl }],
  templateUrl: './product-list.html',
  styleUrl: './product-list.scss'
})
export class ProductListComponent implements OnInit, OnDestroy {
  private productService = inject(ProductService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private dialog = inject(MatDialog);
  private snackBar = inject(MatSnackBar);

  readonly state = signal<'loading' | 'error' | 'ready'>('loading');
  readonly products = signal<Product[]>([]);
  readonly categories = signal<string[]>([]);
  readonly query = signal('');
  readonly category = signal('');
  readonly sortKey = signal<SortKey>('name');
  readonly sortDir = signal<'asc' | 'desc'>('asc');
  readonly pageIndex = signal(0);
  readonly pageSize = signal(10);
  readonly announcement = signal('');

  readonly hasFilters = computed(() => !!this.query() || !!this.category());

  readonly sorted = computed(() => {
    const key = this.sortKey();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    const rank = (p: Product) => ({ out: 0, low: 1, ok: 2 }[this.status(p)]);
    return [...this.products()].sort((a, b) => {
      let r: number;
      switch (key) {
        case 'name': r = a.name.localeCompare(b.name, 'pt-BR'); break;
        case 'category': r = a.category.localeCompare(b.category, 'pt-BR'); break;
        case 'quantity': r = a.quantity - b.quantity; break;
        case 'price': r = a.price - b.price; break;
        default: r = rank(a) - rank(b);
      }
      return r * dir || a.name.localeCompare(b.name, 'pt-BR');
    });
  });

  readonly pageItems = computed(() => {
    const start = this.pageIndex() * this.pageSize();
    return this.sorted().slice(start, start + this.pageSize());
  });

  /** Opções de ordenação para a lista em cards (mobile) */
  readonly sortOptions: { value: string; label: string }[] = [
    { value: 'name:asc', label: 'Nome (A–Z)' },
    { value: 'name:desc', label: 'Nome (Z–A)' },
    { value: 'quantity:asc', label: 'Menor estoque' },
    { value: 'quantity:desc', label: 'Maior estoque' },
    { value: 'price:asc', label: 'Menor preço' },
    { value: 'price:desc', label: 'Maior preço' },
    { value: 'status:asc', label: 'Mais críticos' }
  ];
  readonly sortValue = computed(() => `${this.sortKey()}:${this.sortDir()}`);

  private seq = 0;

  ngOnInit(): void {
    this.productService.getCategories().subscribe({ next: (c) => this.categories.set(c), error: () => {} });
    // A busca global (topbar) chega como ?q=
    this.route.queryParamMap.subscribe((params) => {
      this.query.set((params.get('q') ?? '').slice(0, 100));
      this.pageIndex.set(0);
      this.load();
    });
  }

  load(): void {
    this.flushAll(); // ajustes pendentes saem antes de a lista ser substituída
    const id = ++this.seq;
    this.state.set('loading');
    this.productService.getProducts(this.query(), this.category()).subscribe({
      next: (list) => {
        if (id !== this.seq) return;
        this.products.set(list);
        this.state.set('ready');
        this.announcement.set(`${list.length} ${list.length === 1 ? 'produto encontrado' : 'produtos encontrados'}`);
      },
      error: () => { if (id === this.seq) this.state.set('error'); }
    });
  }

  // ---------- busca / filtros ----------
  submitSearch(value: string): void {
    const q = value.trim().slice(0, 100);
    this.router.navigate([], { queryParams: { q: q || null }, queryParamsHandling: 'merge' });
  }

  setCategory(value: string): void {
    this.category.set(value);
    this.pageIndex.set(0);
    this.load();
  }

  clearFilters(): void {
    this.category.set('');
    if (this.query()) this.router.navigate([], { queryParams: { q: null }, queryParamsHandling: 'merge' });
    else { this.pageIndex.set(0); this.load(); }
  }

  // ---------- ordenação / paginação ----------
  sortBy(key: SortKey): void {
    if (this.sortKey() === key) this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    else { this.sortKey.set(key); this.sortDir.set('asc'); }
    this.pageIndex.set(0);
  }

  setSort(value: string): void {
    const [k, d] = value.split(':');
    this.sortKey.set(k as SortKey);
    this.sortDir.set(d as 'asc' | 'desc');
    this.pageIndex.set(0);
  }

  ariaSort(key: SortKey): 'ascending' | 'descending' | 'none' {
    if (this.sortKey() !== key) return 'none';
    return this.sortDir() === 'asc' ? 'ascending' : 'descending';
  }

  onPage(e: PageEvent): void {
    this.pageIndex.set(e.pageIndex);
    this.pageSize.set(e.pageSize);
  }

  // ---------- status ----------
  status(p: Product): Status {
    if (p.quantity === 0) return 'out';
    return p.quantity <= p.minStock ? 'low' : 'ok';
  }

  statusLabel(p: Product): string {
    return { out: 'Esgotado', low: 'Baixo', ok: 'Em estoque' }[this.status(p)];
  }

  code(p: Product): string {
    return '#' + String(p.id).padStart(4, '0');
  }

  onImgError(event: Event): void {
    const img = event.target as HTMLImageElement;
    if (!img.src.endsWith('images/placeholder.png')) img.src = 'images/placeholder.png';
  }

  // ---------- ajuste rápido de quantidade ----------
  // Otimista: a UI muda na hora; os cliques são acumulados e enviados como UM delta
  // (PATCH atômico no servidor). Em erro, só o delta enviado é revertido.
  private deltas = new Map<number, number>();   // delta acumulado ainda não enviado
  private timers = new Map<number, ReturnType<typeof setTimeout>>();
  private inflight = new Map<number, number>(); // requisições em andamento por produto
  private issued = new Map<number, number>();   // nº da última requisição emitida por produto
  private names = new Map<number, string>();    // nome para mensagens (independe da lista atual)

  adjust(p: Product, delta: number): void {
    const cur = this.products().find((x) => x.id === p.id);
    if (!cur) return;
    const next = cur.quantity + delta;
    if (next < 0 || next > MAX_QTY) {
      this.announcement.set(`${cur.name}: estoque ${next < 0 ? 'mínimo (0)' : 'máximo'} atingido`);
      return;
    }
    this.names.set(p.id, cur.name);
    this.patchQuantity(p.id, next);
    this.deltas.set(p.id, (this.deltas.get(p.id) ?? 0) + delta);
    clearTimeout(this.timers.get(p.id));
    this.timers.set(p.id, setTimeout(() => this.flush(p.id), 400));
  }

  /** Envia já todos os ajustes pendentes (antes de recarregar a lista ou sair da página). */
  private flushAll(): void {
    for (const [id, t] of [...this.timers]) {
      clearTimeout(t);
      this.flush(id);
    }
  }

  ngOnDestroy(): void {
    this.flushAll();
  }

  private flush(id: number): void {
    this.timers.delete(id);
    const delta = this.deltas.get(id) ?? 0;
    this.deltas.delete(id);
    if (delta === 0) return;
    const name = this.names.get(id) ?? 'produto';
    const seq = (this.issued.get(id) ?? 0) + 1;
    this.issued.set(id, seq);
    this.inflight.set(id, (this.inflight.get(id) ?? 0) + 1);
    this.productService.adjustQuantity(id, delta).subscribe({
      next: (saved) => {
        this.endInflight(id);
        // Só a resposta da requisição mais recente (e sem ajustes pendentes) define o valor:
        // respostas antigas que chegam fora de ordem são ignoradas.
        if (seq === this.issued.get(id) && !this.deltas.has(id)) this.patchQuantity(id, saved.quantity);
        this.announcement.set(`${name}: ${saved.quantity} em estoque`);
      },
      error: (err: HttpErrorResponse) => {
        this.endInflight(id);
        const current = this.products().find((x) => x.id === id);
        if (current) this.patchQuantity(id, current.quantity - delta);
        const retry = Number(err.headers?.get('Retry-After'));
        const msg =
          err.status === 409 ? `Estoque de "${name}" não pode ficar fora de 0 a 1.000.000. Quantidade restaurada.`
          : err.status === 429 ? `Muitas requisições${retry > 0 ? `. Tente de novo em ${retry} s` : '. Aguarde um instante'}. Quantidade restaurada.`
          : `Não foi possível atualizar "${name}". Quantidade restaurada.`;
        this.snackBar.open(msg, 'Fechar', { duration: 4000 });
      }
    });
  }

  private endInflight(id: number): void {
    const n = (this.inflight.get(id) ?? 1) - 1;
    if (n <= 0) this.inflight.delete(id); else this.inflight.set(id, n);
  }

  private patchQuantity(id: number, quantity: number): void {
    this.products.update((list) => list.map((x) => (x.id === id ? { ...x, quantity } : x)));
  }

  // ---------- exclusão ----------
  confirmDelete(p: Product, trigger: HTMLElement): void {
    const ref = this.dialog.open(DeleteDialogComponent, {
      width: '420px',
      maxWidth: 'calc(100vw - 32px)',
      ariaLabelledBy: 'delete-title',
      ariaDescribedBy: 'delete-desc',
      data: { name: p.name },
      restoreFocus: false
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (!confirmed) { trigger.focus(); return; }
      this.productService.deleteProduct(p.id).subscribe({
        next: () => {
          this.products.update((list) => list.filter((x) => x.id !== p.id));
          const maxPage = Math.max(0, Math.ceil(this.products().length / this.pageSize()) - 1);
          if (this.pageIndex() > maxPage) this.pageIndex.set(maxPage);
          this.snackBar.open(`"${p.name}" excluído`, 'Fechar', { duration: 3000 });
          this.announcement.set(`${p.name} excluído`);
          document.getElementById('product-search')?.focus();
        },
        error: () => {
          this.snackBar.open('Não foi possível excluir o produto.', 'Fechar', { duration: 4000 });
          trigger.focus();
        }
      });
    });
  }
}
