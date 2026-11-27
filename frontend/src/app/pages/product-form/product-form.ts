import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, of, switchMap, map, catchError } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ProductService } from '../../services/product.service';
import { parseProblem } from '../../core/api-error';
import { CreateProduct, Product, VariantInput, isSingle, productImageUrl } from '../../models/product.model';

interface Cell { id?: number; quantity: number; minStock: number; }

const MAX_UPLOAD = 2 * 1024 * 1024;
const MAX_SIDE = 1600;
const DEFAULT_CATEGORIES = ['Calças', 'Camisetas', 'Camisas', 'Bermudas', 'Vestidos', 'Saias', 'Jaquetas', 'Moletons', 'Acessórios'];
const PRESETS: { label: string; sizes: string[] }[] = [
  { label: 'P, M, G, GG', sizes: ['P', 'M', 'G', 'GG'] },
  { label: 'PP ao GG', sizes: ['PP', 'P', 'M', 'G', 'GG'] },
  { label: '36 ao 46', sizes: ['36', '38', '40', '42', '44', '46'] },
  { label: '1, 2, 4, 6, 8 (infantil)', sizes: ['1', '2', '4', '6', '8'] }
];

/**
 * Cadastro e edição. O dono tira a foto pelo celular, preenche o básico e monta a grade:
 * tamanhos (e materiais, se houver) e quantas peças tem de cada combinação.
 */
@Component({
  selector: 'app-product-form',
  standalone: true,
  imports: [RouterLink, MatIconModule],
  templateUrl: './product-form.html',
  styleUrl: './product-form.scss'
})
export class ProductFormComponent {
  private api = inject(ProductService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private snack = inject(MatSnackBar);
  readonly presets = PRESETS;

  readonly productId = signal<number | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly submitted = signal(false);
  readonly categories = signal<string[]>(DEFAULT_CATEGORIES);

  // dados
  readonly name = signal('');
  readonly brand = signal('');
  readonly category = signal('');
  /** preço e custo como o dono digita ("299,90"); o número sai na hora de validar/salvar */
  readonly priceText = signal('');
  readonly costText = signal('');
  readonly price = computed(() => this.num(this.priceText()));
  readonly cost = computed(() => this.num(this.costText()));
  readonly description = signal('');

  // grade
  readonly hasGrid = signal(true);
  readonly sizes = signal<string[]>([]);
  readonly materials = signal<string[]>([]);
  readonly cells = signal<Record<string, Cell>>({});
  readonly single = signal<Cell>({ quantity: 0, minStock: 0 });
  readonly newSize = signal('');
  readonly newMaterial = signal('');
  /** saldos de quando a tela abriu, para avisar quando tirar um tamanho que tem peças */
  private original = new Map<string, number>();

  // foto
  readonly imagePreview = signal<string | null>(null);
  readonly imageError = signal('');
  readonly imageBusy = signal(false);
  private imageFile: File | null = null;
  private removeImage = false;
  private hadImage = false;

  readonly isEdit = computed(() => this.productId() !== null);
  readonly mats = computed(() => (this.materials().length ? this.materials() : ['']));
  readonly variants = computed<VariantInput[]>(() => {
    if (!this.hasGrid()) {
      const c = this.single();
      return [{ id: c.id, size: 'Único', material: '', quantity: c.quantity, minStock: c.minStock }];
    }
    const out: VariantInput[] = [];
    for (const m of this.mats())
      for (const s of this.sizes()) {
        const c = this.cells()[this.key(s, m)] ?? { quantity: 0, minStock: 1 };
        out.push({ id: c.id, size: s, material: m, quantity: c.quantity, minStock: c.minStock });
      }
    return out;
  });
  readonly total = computed(() => this.variants().reduce((s, v) => s + v.quantity, 0));
  /** peças que saem do estoque porque o tamanho/material foi tirado da grade */
  readonly removedStock = computed(() => {
    const keep = new Set(this.variants().map((v) => this.key(v.size, v.material)));
    let n = 0;
    this.original.forEach((qty, k) => { if (!keep.has(k)) n += qty; });
    return n;
  });

  readonly errors = computed(() => {
    const e: Record<string, string> = {};
    if (!this.name().trim()) e['name'] = 'Escreva o nome da peça.';
    if (!this.category().trim()) e['category'] = 'Escolha ou escreva o tipo de peça.';
    const p = this.price();
    if (p === null || isNaN(p) || p < 0) e['price'] = 'Informe o preço de venda.';
    const c = this.cost();
    if (c !== null && (isNaN(c) || c < 0)) e['cost'] = 'O custo não pode ser negativo.';
    if (this.hasGrid() && this.sizes().length === 0) e['sizes'] = 'Adicione pelo menos um tamanho.';
    return e;
  });

  constructor() {
    this.api.getCategories().subscribe({
      next: (list) => this.categories.set([...new Set([...list, ...DEFAULT_CATEGORIES])].sort((a, b) => a.localeCompare(b, 'pt-BR'))),
      error: () => {}
    });
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.productId.set(+id);
      this.load(+id);
    }
  }

  private key(size: string, material: string): string {
    return `${size.toLowerCase()}|${material.toLowerCase()}`;
  }

  private load(id: number): void {
    this.loading.set(true);
    this.api.getProduct(id).subscribe({
      next: (p) => { this.fill(p); this.loading.set(false); },
      error: () => {
        this.snack.open('Produto não encontrado.', 'Fechar', { duration: 3000 });
        this.router.navigate(['/products']);
      }
    });
  }

  private fill(p: Product): void {
    this.name.set(p.name);
    this.brand.set(p.brand ?? '');
    this.category.set(p.category);
    this.priceText.set(this.money(p.price));
    this.costText.set(p.cost ? this.money(p.cost) : '');
    this.description.set(p.description ?? '');
    this.hadImage = !!p.imageUpdatedAt;
    this.imagePreview.set(productImageUrl(p));
    this.original = new Map(p.variants.map((v) => [this.key(v.size, v.material), v.quantity]));
    if (isSingle(p)) {
      const v = p.variants[0];
      this.hasGrid.set(false);
      this.single.set({ id: v.id, quantity: v.quantity, minStock: v.minStock });
      return;
    }
    this.hasGrid.set(true);
    this.sizes.set([...new Set(p.variants.map((v) => v.size))]);
    this.materials.set([...new Set(p.variants.map((v) => v.material))].filter(Boolean));
    this.cells.set(Object.fromEntries(p.variants.map((v) => [this.key(v.size, v.material), { id: v.id, quantity: v.quantity, minStock: v.minStock }])));
  }

  private money(n: number): string {
    return n.toFixed(2).replace('.', ',');
  }

  /** "1.299,90", "299,9" ou "299.90" viram número; vazio vira null; texto inválido vira NaN. */
  num(value: string): number | null {
    const t = value.trim().replace(/^R\$\s*/, '');
    if (t === '') return null;
    const normalized = t.includes(',') ? t.replace(/\./g, '').replace(',', '.') : t;
    const n = Number(normalized);
    return isNaN(n) ? NaN : Math.round(n * 100) / 100;
  }

  // ---------- grade ----------
  applyPreset(sizes: string[]): void {
    this.sizes.update((cur) => [...cur, ...sizes.filter((s) => !cur.some((c) => c.toLowerCase() === s.toLowerCase()))]);
  }

  addSize(): void {
    const s = this.newSize().trim().toUpperCase().slice(0, 20);
    if (!s) return;
    if (!this.sizes().some((x) => x.toLowerCase() === s.toLowerCase())) this.sizes.update((cur) => [...cur, s]);
    this.newSize.set('');
  }

  removeSize(s: string): void { this.sizes.update((cur) => cur.filter((x) => x !== s)); }

  addMaterial(): void {
    const raw = this.newMaterial().trim().slice(0, 40);
    if (!raw) return;
    const m = raw[0].toUpperCase() + raw.slice(1);
    if (!this.materials().some((x) => x.toLowerCase() === m.toLowerCase())) {
      // o primeiro material herda as quantidades que estavam "sem material"
      if (this.materials().length === 0) {
        this.cells.update((cells) => {
          const next = { ...cells };
          for (const s of this.sizes()) {
            const from = next[this.key(s, '')];
            if (from && !next[this.key(s, m)]) { next[this.key(s, m)] = from; delete next[this.key(s, '')]; }
          }
          return next;
        });
      }
      this.materials.update((cur) => [...cur, m]);
    }
    this.newMaterial.set('');
  }

  removeMaterial(m: string): void { this.materials.update((cur) => cur.filter((x) => x !== m)); }

  cell(size: string, material: string): Cell {
    return this.cells()[this.key(size, material)] ?? { quantity: 0, minStock: 1 };
  }

  setCell(size: string, material: string, field: 'quantity' | 'minStock', value: number): void {
    const n = Math.max(0, Math.min(1000000, Math.floor(Number(value) || 0)));
    const k = this.key(size, material);
    this.cells.update((cells) => ({ ...cells, [k]: { ...this.cell(size, material), [field]: n } }));
  }

  stepCell(size: string, material: string, d: number): void {
    this.setCell(size, material, 'quantity', this.cell(size, material).quantity + d);
  }

  setSingle(field: 'quantity' | 'minStock', value: number): void {
    const n = Math.max(0, Math.min(1000000, Math.floor(Number(value) || 0)));
    this.single.update((c) => ({ ...c, [field]: n }));
  }

  // ---------- foto ----------
  async onImage(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = ''; // permite escolher a mesma foto de novo
    if (!file) return;
    this.imageError.set('');
    this.imageBusy.set(true);
    try {
      const jpeg = await this.shrink(file);
      if (jpeg.size > MAX_UPLOAD) throw new Error('grande');
      this.imageFile = jpeg;
      this.removeImage = false;
      this.imagePreview.set(URL.createObjectURL(jpeg));
    } catch {
      this.imageError.set('Não deu para usar essa foto. Tente tirar outra ou escolher uma imagem JPG ou PNG.');
    } finally {
      this.imageBusy.set(false);
    }
  }

  /** Reduz a foto do celular (que costuma ter vários MB) para JPEG de até 1600px, abaixo do limite de 2 MB. */
  private async shrink(file: File): Promise<File> {
    const bitmap = await createImageBitmap(file);
    const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
    const canvas = document.createElement('canvas');
    canvas.width = Math.round(bitmap.width * scale);
    canvas.height = Math.round(bitmap.height * scale);
    canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close();
    for (const q of [0.85, 0.75, 0.6]) {
      const blob = await new Promise<Blob | null>((r) => canvas.toBlob(r, 'image/jpeg', q));
      if (blob && blob.size <= MAX_UPLOAD) return new File([blob], 'foto.jpg', { type: 'image/jpeg' });
    }
    throw new Error('grande');
  }

  clearImage(): void {
    this.imageFile = null;
    this.imagePreview.set(null);
    this.imageError.set('');
    this.removeImage = this.hadImage;
  }

  private syncImage(product: Product): Observable<boolean> {
    if (this.imageFile) return this.api.uploadImage(product.id, this.imageFile).pipe(map(() => true), catchError(() => of(false)));
    if (this.removeImage) return this.api.deleteImage(product.id).pipe(map(() => true), catchError(() => of(false)));
    return of(true);
  }

  // ---------- salvar ----------
  save(): void {
    this.submitted.set(true);
    if (Object.keys(this.errors()).length) {
      this.snack.open('Confira os campos marcados em vermelho.', 'Fechar', { duration: 4000 });
      return;
    }
    if (this.saving()) return;
    this.saving.set(true);
    const data: CreateProduct = {
      name: this.name().trim(),
      brand: this.brand().trim() || null,
      category: this.category().trim(),
      description: this.description().trim() || undefined,
      price: this.price() ?? 0,
      cost: this.cost() ?? 0,
      variants: this.variants()
    };
    const id = this.productId();
    const save$ = id ? this.api.updateProduct(id, data) : this.api.createProduct(data);
    save$.pipe(switchMap((p) => this.syncImage(p).pipe(map((ok) => ({ p, ok }))))).subscribe({
      next: ({ p, ok }) => {
        const msg = id ? 'Alterações salvas' : 'Produto cadastrado';
        this.snack.open(ok ? `${msg}.` : `${msg}, mas a foto não foi enviada. Tente pôr a foto de novo.`, 'Fechar', { duration: 4500 });
        this.router.navigate(['/products', p.id]);
      },
      error: (err) => {
        this.saving.set(false);
        const p = parseProblem(err);
        const detail = p.errors ? Object.values(p.errors).flat()[0] : null;
        this.snack.open(detail ? `Não foi salvo: ${detail}` : 'Não foi salvo. Verifique a internet e tente de novo.', 'Fechar', { duration: 6000 });
      }
    });
  }
}
