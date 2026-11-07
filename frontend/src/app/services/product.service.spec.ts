import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductService } from './product.service';
import { Product } from '../models/product.model';

const product: Product = {
  id: 5, name: 'Mouse', category: 'Eletrônicos', quantity: 7, price: 10, minStock: 2,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z'
};

describe('ProductService.adjustQuantity', () => {
  let service: ProductService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ProductService);
    http = TestBed.inject(HttpTestingController);
  });

  it('envia PATCH /api/products/{id}/quantity com o delta no corpo', () => {
    let result: Product | undefined;
    service.adjustQuantity(5, -2).subscribe((p) => (result = p));

    const req = http.expectOne('/api/products/5/quantity');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ delta: -2 });
    req.flush({ ...product, quantity: 5 });

    expect(result?.quantity).toBe(5);
    http.verify();
  });

  it('falha por timeout quando o PATCH não responde em 15 s', () => {
    vi.useFakeTimers();
    let error: unknown;
    service.adjustQuantity(5, 1).subscribe({ error: (e) => (error = e) });
    http.expectOne('/api/products/5/quantity'); // sem resposta
    vi.advanceTimersByTime(15000);
    expect((error as Error)?.name).toBe('TimeoutError');
  });

  afterEach(() => vi.useRealTimers());

  it('propaga o erro HTTP (ex.: 409) ao chamador', () => {
    let status = 0;
    service.adjustQuantity(5, -100).subscribe({ error: (e) => (status = e.status) });

    http.expectOne('/api/products/5/quantity').flush({ title: 'Invalid stock quantity' }, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
  });
});
