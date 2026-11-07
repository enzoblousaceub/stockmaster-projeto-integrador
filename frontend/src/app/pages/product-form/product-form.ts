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
      minStock: [0, [Validators.required, Validators.min(0), Validators.max(1000000)]]
    });

    this.loadCategories();

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode = true;
      this.productId = +id;
      this.pageTitle = 'Editar produto';
      this.loadProduct(this.productId);
    }
  }

  /** Imagem ausente: troca pelo placeholder uma única vez (evita loop se ele faltar). */
  onImgError(event: Event): void {
    const img = event.target as HTMLImageElement;
    if (!img.src.endsWith('images/placeholder.png')) img.src = 'images/placeholder.png';
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
          minStock: product.minStock
        });
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

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.loading = true;
    const data = this.form.value;

    if (this.isEditMode && this.productId) {
      this.productService.updateProduct(this.productId, data).subscribe({
        next: () => {
          this.snackBar.open('Produto atualizado com sucesso', 'Fechar', { duration: 3000 });
          this.router.navigate(['/products']);
        },
        error: () => {
          this.snackBar.open('Falha ao atualizar produto', 'Fechar', { duration: 3000 });
          this.loading = false;
          this.cdr.markForCheck();
        }
      });
    } else {
      this.productService.createProduct(data).subscribe({
        next: () => {
          this.snackBar.open('Produto criado com sucesso', 'Fechar', { duration: 3000 });
          this.router.navigate(['/products']);
        },
        error: () => {
          this.snackBar.open('Falha ao criar produto', 'Fechar', { duration: 3000 });
          this.loading = false;
          this.cdr.markForCheck();
        }
      });
    }
  }
}
