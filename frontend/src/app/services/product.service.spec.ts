import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductService } from './product.service';
import { Product } from '../models/product.model';

const product: Product = {
  id: 5, name: 'Mouse', category: 'Eletrônicos', quantity: 7, price: 10, cost: 6, minStock: 2,
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

describe('ProductService: movimentações, imagem e relatórios', () => {
  let service: ProductService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(ProductService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('POST /api/products/{id}/movements com o corpo da movimentação', () => {
    service.addMovement(5, { type: 'Saida', quantity: 2, reason: 'Venda' }).subscribe();
    const req = http.expectOne('/api/products/5/movements');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ type: 'Saida', quantity: 2, reason: 'Venda' });
    req.flush({});
  });

  it('PUT da imagem como multipart (campo "file")', () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'foto.png', { type: 'image/png' });
    service.uploadImage(5, file).subscribe();
    const req = http.expectOne('/api/products/5/image');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body instanceof FormData).toBe(true);
    expect((req.request.body as FormData).get('file')).toBeTruthy();
    req.flush(product);
  });

  it('GET /api/reports/summary com o período', () => {
    service.getReport(90).subscribe();
    const req = http.expectOne((r) => r.url === '/api/reports/summary');
    expect(req.request.params.get('days')).toBe('90');
    req.flush({});
  });
});
