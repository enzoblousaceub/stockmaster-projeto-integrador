import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Product, CreateProduct, UpdateProduct, DashboardStats } from '../models/product.model';

@Injectable({
  providedIn: 'root'
})
export class ProductService {
  // 🔥 MUDE PARA URL RELATIVA (funciona em qualquer ambiente)
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
    return this.http.patch<Product>(`${this.apiUrl}/${id}/quantity`, { delta });
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
}