import { Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { formatDate } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { GarmentComponent } from '../../components/garment/garment';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ProductService } from '../../services/product.service';
import { AuthService } from '../../core/auth.service';
import { parseProblem } from '../../core/api-error';
import {
  PAYMENT_LABEL, PAYMENT_METHODS, PaymentMethod, Product, Sale, Variant, isSingle, levelOf, materialsOf, monogram,
  productImageUrl, variantText
} from '../../models/product.model';

/** Item na sacola: guarda o que precisa para mostrar mesmo se a lista de produtos recarregar. */
interface BagItem {
  variantId: number;
  productId: number;
  productName: string;
  size: string;
  material: string;
  image: string | null;
  qty: number;
}

const BAG_KEY = 'sm-sacola';

/**
 * Venda no balcão. O vendedor monta a sacola (várias peças, pode mudar ou tirar à vontade), escolhe a forma de
 * pagamento e confirma. A venda é gravada de uma vez: se a internet falhar, nada fica pela metade e a sacola continua.
 * Logo depois dá para desfazer por 5 minutos.
 */
@Component({
  selector: 'app-sale',
  standalone: true,
  imports: [MatIconModule, GarmentComponent],
  templateUrl: './sale.html',
  styleUrl: './sale.scss'
})
export class SaleComponent {
  private api = inject(ProductService);
  private snack = inject(MatSnackBar);
  readonly auth = inject(AuthService);

  readonly payments = PAYMENT_METHODS;
  readonly paymentLabel = PAYMENT_LABEL;
  readonly variantText = variantText;
  readonly monogram = monogram;
  readonly imageOf = productImageUrl;

  // ---------- catálogo ----------
  readonly state = signal<'loading' | 'error' | 'ready'>('loading');
  readonly products = signal<Product[]>([]);
  readonly search = signal('');
  readonly category = signal('');
  readonly categories = computed(() => [...new Set(this.products().map((p) => p.category))].sort((a, b) => a.localeCompare(b, 'pt-BR')));
  readonly visible = computed(() => {
    const q = this.norm(this.search());
    const c = this.category();
    return this.products()
      .filter((p) => !c || p.category === c)
      .filter((p) => !q || this.norm(`${p.name} ${p.brand ?? ''} ${p.category}`).includes(q))
      .sort((a, b) => Number(a.quantity === 0) - Number(b.quantity === 0) || a.name.localeCompare(b.name, 'pt-BR'));
  });

  // ---------- escolha de tamanho ----------
  readonly picking = signal<Product | null>(null);
  readonly pickMaterial = signal('');
  readonly pickVariant = signal<Variant | null>(null);
  readonly pickQty = signal(1);
  readonly pickMaterials = computed(() => (this.picking() ? materialsOf(this.picking()!) : []));
  readonly pickSizes = computed(() => this.picking()?.variants.filter((v) => v.material === this.pickMaterial()) ?? []);
  readonly pickSingle = computed(() => (this.picking() ? isSingle(this.picking()!) : false));

  // ---------- sacola ----------
  readonly bag = signal<BagItem[]>(this.loadBag());
  readonly bagOpen = signal(false);
  readonly bagCount = computed(() => this.bag().reduce((s, i) => s + i.qty, 0));
  readonly payment = signal<PaymentMethod | null>(null);
  readonly sending = signal(false);
  /** variações que o servidor disse não ter saldo suficiente (destacadas na sacola) */
  readonly short = signal<Record<number, number>>({});

  // ---------- depois da venda ----------
  readonly done = signal<Sale | null>(null);
  readonly undoing = signal(false);
  readonly recent = signal<Sale[]>([]);
  /** segundos que faltam para poder desfazer, por venda (relógio local, a partir da resposta do servidor) */
  private deadlines = new Map<number, number>();
  readonly now = signal(Date.now());

  constructor() {
    this.load();
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
    // a sacola sobrevive a recarregar a página ou perder a conexão
    effect(() => {
      const items = this.bag();
      try { sessionStorage.setItem(BAG_KEY, JSON.stringify(items)); } catch { /* armazenamento indisponível */ }
    });
  }

  load(): void {
    this.state.set('loading');
    this.api.getProducts().subscribe({
      next: (list) => { this.products.set(list); this.state.set('ready'); },
      error: () => this.state.set('error')
    });
    this.loadRecent();
  }

  private loadRecent(): void {
    this.api.getSales(10).subscribe({
      next: (list) => {
        list.forEach((s) => this.track(s));
        this.recent.set(list);
      },
      error: () => { /* lista complementar: a venda funciona sem ela */ }
    });
  }

  private norm(s: string): string {
    return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
  }

  // ---------- catálogo ----------
  /** Quanto ainda dá para pôr na sacola (saldo menos o que já está nela). */
  free(v: Variant): number {
    const inBag = this.bag().find((i) => i.variantId === v.id)?.qty ?? 0;
    return Math.max(0, v.quantity - inBag);
  }

  open(p: Product): void {
    if (p.quantity === 0) return;
    this.picking.set(p);
    const mats = materialsOf(p);
    // começa no primeiro material que tem peça disponível
    const firstMat = mats.find((m) => p.variants.some((v) => v.material === m && this.free(v) > 0)) ?? mats[0];
    this.pickMaterial.set(firstMat);
    this.pickVariant.set(isSingle(p) ? p.variants[0] : null);
    this.pickQty.set(1);
  }

  closePick(): void { this.picking.set(null); }

  chooseMaterial(m: string): void {
    this.pickMaterial.set(m);
    this.pickVariant.set(null);
    this.pickQty.set(1);
  }

  chooseSize(v: Variant): void {
    if (this.free(v) === 0) return;
    this.pickVariant.set(v);
    this.pickQty.set(1);
  }

  stepPick(d: number): void {
    const v = this.pickVariant();
    if (!v) return;
    this.pickQty.set(Math.min(this.free(v), Math.max(1, this.pickQty() + d)));
  }

  addToBag(): void {
    const p = this.picking();
    const v = this.pickVariant();
    if (!p || !v || this.free(v) === 0) return;
    const qty = Math.min(this.pickQty(), this.free(v));
    this.bag.update((items) => {
      const found = items.find((i) => i.variantId === v.id);
      if (found) return items.map((i) => (i === found ? { ...i, qty: i.qty + qty } : i));
      return [...items, { variantId: v.id, productId: p.id, productName: p.name, size: v.size, material: v.material, image: productImageUrl(p), qty }];
    });
    this.picking.set(null);
    this.snack.open(`${qty} ${qty === 1 ? 'peça adicionada' : 'peças adicionadas'} à sacola`, undefined, { duration: 1800 });
  }

  // ---------- sacola ----------
  private variantById(id: number): Variant | undefined {
    for (const p of this.products()) {
      const v = p.variants.find((x) => x.id === id);
      if (v) return v;
    }
    return undefined;
  }

  /** Saldo atual da variação (para o + da sacola não passar do estoque). */
  stockOf(item: BagItem): number {
    return this.variantById(item.variantId)?.quantity ?? item.qty;
  }

  stepBag(item: BagItem, d: number): void {
    const next = item.qty + d;
    if (next < 1) return this.remove(item);
    if (next > this.stockOf(item)) return;
    this.bag.update((items) => items.map((i) => (i.variantId === item.variantId ? { ...i, qty: next } : i)));
    this.clearShort(item.variantId);
  }

  remove(item: BagItem): void {
    this.bag.update((items) => items.filter((i) => i.variantId !== item.variantId));
    this.clearShort(item.variantId);
    if (this.bag().length === 0) this.bagOpen.set(false);
  }

  clearBag(): void {
    this.bag.set([]);
    this.short.set({});
    this.payment.set(null);
    this.bagOpen.set(false);
  }

  private clearShort(id: number): void {
    const s = { ...this.short() };
    delete s[id];
    this.short.set(s);
  }

  itemText(i: Pick<BagItem, 'size' | 'material'>): string {
    if (i.size === 'Único' && !i.material) return 'Tamanho único';
    return i.material ? `Tamanho ${i.size}, ${i.material}` : `Tamanho ${i.size}`;
  }

  confirm(): void {
    const payment = this.payment();
    if (!payment || this.bag().length === 0 || this.sending()) return;
    this.sending.set(true);
    this.api.createSale({ paymentMethod: payment, items: this.bag().map((i) => ({ variantId: i.variantId, quantity: i.qty })) }).subscribe({
      next: (sale) => {
        this.sending.set(false);
        this.track(sale);
        this.done.set(sale);
        this.recent.update((list) => [sale, ...list.filter((s) => s.id !== sale.id)].slice(0, 10));
        this.bag.set([]);
        this.short.set({});
        this.payment.set(null);
        this.bagOpen.set(false);
        this.refreshStock();
      },
      error: (err) => {
        this.sending.set(false);
        const p = parseProblem(err);
        if (p.code === 'insufficient_stock') {
          const body = err instanceof HttpErrorResponse ? err.error : null;
          const shortages: { variantId: number; available: number }[] = Array.isArray(body?.shortages) ? body.shortages : [];
          this.short.set(Object.fromEntries(shortages.map((s) => [s.variantId, s.available])));
          this.snack.open('Alguma peça acabou enquanto você montava a venda. Ajuste os itens marcados e confirme de novo.', 'Fechar', { duration: 7000 });
          this.refreshStock();
        } else if (p.code === 'variant_not_found') {
          this.snack.open('Um item foi removido do cadastro. Tire-o da sacola e confirme de novo.', 'Fechar', { duration: 7000 });
          this.refreshStock();
        } else if (p.status <= 0) {
          // sem rede (0) ou demorou demais (timeout): o servidor pode não ter recebido; a sacola fica
          this.snack.open('Sem resposta do sistema. Confira em "Últimas vendas" se ela entrou antes de confirmar de novo. A sacola continua aqui.', 'Fechar', { duration: 9000 });
          this.loadRecent();
        } else {
          this.snack.open('A venda não foi registrada. A sacola continua aqui; tente de novo.', 'Fechar', { duration: 6000 });
        }
      }
    });
  }

  private refreshStock(): void {
    this.api.getProducts().subscribe({ next: (list) => this.products.set(list), error: () => {} });
  }

  // ---------- desfazer ----------
  private track(s: Sale): void {
    if (this.deadlines.has(s.id)) return;
    // relógio do aparelho e do servidor nunca batem exato: nunca mostra mais que os 5 minutos da regra
    const window = Math.min(new Date(s.undoDeadline).getTime() - Date.now(), 5 * 60 * 1000);
    this.deadlines.set(s.id, Date.now() + Math.max(0, window));
  }

  /** Segundos restantes para desfazer (0 = não dá mais). */
  remaining(s: Sale): number {
    if (s.undoneAt || !s.canUndo) return 0;
    const end = this.deadlines.get(s.id) ?? 0;
    return Math.max(0, Math.ceil((end - this.now()) / 1000));
  }

  clock(seconds: number): string {
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  }

  undo(s: Sale): void {
    if (this.undoing()) return;
    this.undoing.set(true);
    this.api.undoSale(s.id).subscribe({
      next: (updated) => {
        this.undoing.set(false);
        this.recent.update((list) => list.map((x) => (x.id === updated.id ? updated : x)));
        if (this.done()?.id === updated.id) this.done.set(null);
        this.snack.open('Venda desfeita. As peças voltaram para o estoque.', 'Fechar', { duration: 4000 });
        this.refreshStock();
      },
      error: (err) => {
        this.undoing.set(false);
        const p = parseProblem(err);
        const msg = p.code === 'undo_expired'
          ? 'O prazo de 5 minutos acabou. Peça ao dono da loja para corrigir o estoque.'
          : p.code === 'already_undone' ? 'Esta venda já tinha sido desfeita.' : 'Não deu para desfazer agora. Tente de novo.';
        this.snack.open(msg, 'Fechar', { duration: 6000 });
        this.loadRecent();
      }
    });
  }

  closeDone(): void { this.done.set(null); }

  /** "26/11 14:13, desfeita, por joana@loja.com" */
  saleMeta(s: Sale): string {
    const parts = [formatDate(s.createdAt, 'dd/MM HH:mm', 'pt-BR')];
    if (s.undoneAt) parts.push('desfeita');
    if (this.auth.isAdmin() && s.createdBy && s.createdBy !== 'seed' && s.createdBy !== this.auth.user()?.email) parts.push(`por ${s.createdBy}`);
    return parts.join(', ');
  }

  private loadBag(): BagItem[] {
    try {
      const raw = sessionStorage.getItem(BAG_KEY);
      const items = raw ? JSON.parse(raw) : [];
      return Array.isArray(items) ? items.filter((i) => i && typeof i.variantId === 'number' && i.qty > 0) : [];
    } catch {
      return [];
    }
  }

  levelOf = levelOf;
}
