import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AbstractControl, ReactiveFormsModule, FormBuilder, FormGroup, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ProductService } from '../../services/product.service';
import { Product, productImageUrl } from '../../models/product.model';
import { Observable, of, switchMap, map, catchError } from 'rxjs';

const MAX_IMAGE_BYTES = 2 * 1024 * 1024;
const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

/** Preço com no máximo 2 casas decimais. */
function twoDecimals(c: AbstractControl): ValidationErrors | null {
  const v = c.value;
  if (v === null || v === '' || typeof v !== 'number') return null;
  return Math.abs(v * 100 - Math.round(v * 100)) > 1e-6 ? { decimals: true } : null;
}

@Component({
  selector: 'app-product-form',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, RouterModule,
    MatIconModule,
    MatInputModule, MatFormFieldModule, MatSelectModule,
    MatSnackBarModule
  ],
  templateUrl: './product-form.html',
  styleUrl: './product-form.scss'
})
export class ProductFormComponent implements OnInit {
  form!: FormGroup;
  isEditMode = false;
  productId: number | null = null;
  loading = false;
  categories: string[] = [];
  pageTitle = 'Novo produto';
  previewOpen = false;
  /** imagem atual (servidor) ou escolhida (data URL) para a pré-visualização */
  imagePreview: string | null = null;
  imageError = '';
  private imageFile: File | null = null;
  private removeImage = false;
  private hadImage = false;

  constructor(
    private fb: FormBuilder,
    private productService: ProductService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', [Validators.maxLength(500)]],
      category: ['', [Validators.required, Validators.maxLength(100)]],
      quantity: [0, [Validators.required, Validators.min(0), Validators.max(1000000)]],
      price: [0, [Validators.required, Validators.min(0), Validators.max(1000000), twoDecimals]],
      cost: [0, [Validators.required, Validators.min(0), Validators.max(1000000), twoDecimals]],
      minStock: [0, [Validators.required, Validators.min(0), Validators.max(1000000)]]
    });

    this.loadCategories();

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode = true;
      this.productId = +id;
      this.pageTitle = 'Editar produto';
      // na edição o saldo muda só por movimentação (entrada/saída/conferência), para manter o histórico
      this.form.get('quantity')?.disable();
      this.loadProduct(this.productId);
    }
  }

  loadProduct(id: number): void {
    this.loading = true;
    this.productService.getProduct(id).subscribe({
      next: (product) => {
        this.form.patchValue({
          name: product.name,
          description: product.description || '',
          category: product.category,
          quantity: product.quantity,
          price: product.price,
          cost: product.cost ?? 0,
          minStock: product.minStock
        });
        this.hadImage = !!product.imageUpdatedAt;
        this.imagePreview = productImageUrl(product);
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.snackBar.open('Produto não encontrado', 'Fechar', { duration: 3000 });
        this.loading = false;
        this.cdr.markForCheck();
        this.router.navigate(['/products']);
      }
    });
  }

  loadCategories(): void {
    this.productService.getCategories().subscribe({
      next: (categories) => {
        this.categories = categories;
        this.cdr.markForCheck();
      }
    });
  }

  // ---------- imagem (RF003) ----------
  onImageSelected(input: HTMLInputElement): void {
    const file = input.files?.[0];
    input.value = ''; // permite escolher o mesmo arquivo de novo
    if (!file) return;
    if (!IMAGE_TYPES.includes(file.type)) { this.imageError = 'Use uma imagem JPEG, PNG ou WebP.'; return; }
    if (file.size > MAX_IMAGE_BYTES) { this.imageError = 'A imagem deve ter no máximo 2 MB.'; return; }
    this.imageError = '';
    this.imageFile = file;
    this.removeImage = false;
    const reader = new FileReader();
    reader.onload = () => { this.imagePreview = reader.result as string; this.cdr.markForCheck(); };
    reader.readAsDataURL(file);
  }

  clearImage(): void {
    this.imageFile = null;
    this.imagePreview = null;
    this.imageError = '';
    this.removeImage = this.hadImage;
  }

  /** Envia/remove a imagem depois de salvar os dados. Falha na imagem não desfaz o cadastro: só avisa. */
  private syncImage(product: Product): Observable<boolean> {
    if (this.imageFile) return this.productService.uploadImage(product.id, this.imageFile).pipe(map(() => true), catchError(() => of(false)));
    if (this.removeImage) return this.productService.deleteImage(product.id).pipe(map(() => true), catchError(() => of(false)));
    return of(true);
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading = true;
    const { quantity, ...rest } = this.form.getRawValue();
    const data = { ...rest, quantity: Number(quantity) || 0 };
    const editing = this.isEditMode && this.productId;
    const save$ = editing ? this.productService.updateProduct(this.productId!, data) : this.productService.createProduct(data);

    save$.pipe(switchMap((product) => this.syncImage(product))).subscribe({
      next: (imageOk) => {
        const msg = editing ? 'Produto atualizado com sucesso' : 'Produto criado com sucesso';
        this.snackBar.open(imageOk ? msg : `${msg}, mas a imagem não pôde ser salva`, 'Fechar', { duration: 4000 });
        this.router.navigate(['/products']);
      },
      error: () => {
        this.snackBar.open(editing ? 'Falha ao atualizar produto' : 'Falha ao criar produto', 'Fechar', { duration: 3000 });
        this.loading = false;
        this.cdr.markForCheck();
      }
    });
  }
}
