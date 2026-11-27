import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductListComponent } from './product-list';
import { ProductService } from '../../services/product.service';
import { Product, Variant } from '../../models/product.model';
import { AuthService } from '../../core/auth.service';

const v = (id: number, size: string, material: string, quantity: number, minStock = 1): Variant =>
  ({ id, productId: 1, size, material, quantity, minStock, sortOrder: id, label: material ? `${size} · ${material}` : size });

const mk = (id: number, name: string, category: string, variants: Variant[], brand: string | null = null): Product => ({
  id, name, category, brand, variants, quantity: variants.reduce((s, x) => s + x.quantity, 0), price: 10, cost: 6,
  minStock: variants.reduce((s, x) => s + x.minStock, 0), createdAt: '', updatedAt: ''
});

const calca = mk(1, 'Calça Jeans Slim', 'Calças', [v(1, '38', 'Jeans', 0), v(2, '40', 'Jeans', 1), v(3, '42', 'Jeans', 5), v(4, '44', 'Jeans', 3), v(5, '38', 'Sarja', 2, 3)], 'Calvin Klein');
const meia = mk(2, 'Meia Cano Alto', 'Acessórios', [v(6, 'Único', '', 10)], 'Lupo');
const camiseta = mk(3, 'Camiseta Básica', 'Camisetas', [v(7, 'P', '', 4), v(8, 'M', '', 4)]);

describe('ProductListComponent', () => {
  let service: { getProducts: ReturnType<typeof vi.fn> };
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  const canWrite = signal(true);
  const canMove = signal(true);

  function setup(list: Product[] = [calca, meia, camiseta]): ProductListComponent {
    service.getProducts.mockReturnValue(of(list));
    const fixture = TestBed.createComponent(ProductListComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    canWrite.set(true);
    canMove.set(true);
    service = { getProducts: vi.fn() };
    query = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: ProductService, useValue: service },
        { provide: AuthService, useValue: { canWrite, canMove } },
        { provide: ActivatedRoute, useValue: { queryParamMap: query } }
      ]
    });
  });

  it('lista em ordem alfabética e monta as categorias sem repetir', () => {
    const cmp = setup();
    expect(cmp.visible().map((p) => p.name)).toEqual(['Calça Jeans Slim', 'Camiseta Básica', 'Meia Cano Alto']);
    expect(cmp.categories()).toEqual(['Acessórios', 'Calças', 'Camisetas']);
  });

  it('busca por nome ou marca, sem diferenciar acento e maiúscula', () => {
    const cmp = setup();
    cmp.onSearch('calca');
    expect(cmp.visible().map((p) => p.id)).toEqual([1]);
    cmp.onSearch('LUPO');
    expect(cmp.visible().map((p) => p.id)).toEqual([2]);
  });

  it('filtra por categoria', () => {
    const cmp = setup();
    cmp.category.set('Camisetas');
    expect(cmp.visible().map((p) => p.id)).toEqual([3]);
  });

  it('usa ?q= da URL como busca inicial', () => {
    query.next(convertToParamMap({ q: 'meia' }));
    const cmp = setup();
    expect(cmp.search()).toBe('meia');
    expect(cmp.visible().map((p) => p.id)).toEqual([2]);
  });

  it('resume a grade da linha', () => {
    const cmp = setup();
    expect(cmp.gridText(calca)).toBe('38 ao 44, Jeans e Sarja');
    expect(cmp.gridText(meia)).toBe('Tamanho único');
    expect(cmp.gridText(camiseta)).toBe('P, M');
  });

  it('conta tamanhos esgotados e no mínimo por variação', () => {
    const cmp = setup();
    expect(cmp.alert(calca)).toEqual({ out: 1, low: 2 });
    expect(cmp.alert(meia)).toEqual({ out: 0, low: 0 });
  });

  it('modo "chegou mercadoria" só vale para quem pode dar entrada', () => {
    query.next(convertToParamMap({ modo: 'entrada' }));
    canMove.set(false);
    expect(setup().arrival()).toBe(false);
  });

  it('modo "chegou mercadoria" liga com ?modo=entrada', () => {
    query.next(convertToParamMap({ modo: 'entrada' }));
    expect(setup().arrival()).toBe(true);
  });

  it('mostra erro quando a lista não carrega', () => {
    service.getProducts.mockReturnValue(throwError(() => new Error('offline')));
    const fixture = TestBed.createComponent(ProductListComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.state()).toBe('error');
  });
});
