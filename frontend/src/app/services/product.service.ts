import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, timeout } from 'rxjs';
import { Product, CreateProduct, UpdateProduct, DashboardStats, Movement, MovementResult, NewMovement, ReportSummary } from '../models/product.model';

@Injectable({
  providedIn: 'root'
})
export class ProductService {
  // URL relativa: em dev passa pelo proxy; em produção o front é servido pelo mesmo host da API
  private readonly apiUrl = '/api/products';

  constructor(private http: HttpClient) { }

  getProducts(search?: string, category?: string): Observable<Product[]> {
    let params = new HttpParams();
    if (search) params = params.set('search', search);
    if (category) params = params.set('category', category);
    return this.http.get<Product[]>(this.apiUrl, { params });
  }

  getProduct(id: number): Observable<Product> {
    return this.http.get<Product>(`${this.apiUrl}/${id}`);
  }

  createProduct(product: CreateProduct): Observable<Product> {
    return this.http.post<Product>(this.apiUrl, product);
  }

  updateProduct(id: number, product: UpdateProduct): Observable<Product> {
    return this.http.put<Product>(`${this.apiUrl}/${id}`, product);
  }

  /** Ajuste atômico de quantidade no servidor (sem lost update). 409 = fora de 0..1.000.000. */
  adjustQuantity(id: number, delta: number): Observable<Product> {
    return this.http.patch<Product>(`${this.apiUrl}/${id}/quantity`, { delta }).pipe(timeout(15000));
  }

  deleteProduct(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }

  getStats(): Observable<DashboardStats> {
    return this.http.get<DashboardStats>(`${this.apiUrl}/stats`);
  }

  getCategories(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/categories`);
  }

  // ---------- movimentações ----------
  addMovement(productId: number, movement: NewMovement): Observable<MovementResult> {
    return this.http.post<MovementResult>(`${this.apiUrl}/${productId}/movements`, movement).pipe(timeout(15000));
  }

  getProductMovements(productId: number, limit = 100): Observable<Movement[]> {
    return this.http.get<Movement[]>(`${this.apiUrl}/${productId}/movements`, { params: { limit } });
  }

  getMovements(filter: { type?: string; limit?: number } = {}): Observable<Movement[]> {
    let params = new HttpParams().set('limit', filter.limit ?? 50);
    if (filter.type) params = params.set('type', filter.type);
    return this.http.get<Movement[]>('/api/movements', { params });
  }

  // ---------- imagem ----------
  uploadImage(productId: number, file: File): Observable<Product> {
    const form = new FormData();
    form.append('file', file);
    return this.http.put<Product>(`${this.apiUrl}/${productId}/image`, form);
  }

  deleteImage(productId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${productId}/image`);
  }

  // ---------- relatórios ----------
  getReport(days = 30): Observable<ReportSummary> {
    return this.http.get<ReportSummary>('/api/reports/summary', { params: { days } });
  }
}
