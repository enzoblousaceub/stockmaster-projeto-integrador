import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, Subject, of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductListComponent } from './product-list';
import { ProductService } from '../../services/product.service';
import { Product } from '../../models/product.model';

const mk = (id: number, quantity: number): Product => ({
  id, name: `Produto ${id}`, category: 'Cat', quantity, price: 10, minStock: 2,
  createdAt: '', updatedAt: ''
});

describe('ProductListComponent: ajuste rápido de quantidade (+/-)', () => {
  let service: {
    getProducts: ReturnType<typeof vi.fn>;
    getCategories: ReturnType<typeof vi.fn>;
    adjustQuantity: ReturnType<typeof vi.fn>;
    deleteProduct: ReturnType<typeof vi.fn>;
  };
  let snack: { open: ReturnType<typeof vi.fn> };
  let cmp: ProductListComponent;

  const qty = (id = 1) => cmp.products().find((p) => p.id === id)!.quantity;

  function setup(initial: Product[] = [mk(1, 5)]) {
    service.getProducts.mockReturnValue(of(initial));
    const fixture = TestBed.createComponent(ProductListComponent);
    fixture.detectChanges();
    cmp = fixture.componentInstance;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    service = {
      getProducts: vi.fn(),
      getCategories: vi.fn(() => of([])),
      adjustQuantity: vi.fn(),
      deleteProduct: vi.fn()
    };
    snack = { open: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),
        { provide: ProductService, useValue: service },
        { provide: MatSnackBar, useValue: snack },
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({})) } }
      ]
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('acumula cliques em UM delta e muda a UI na hora', () => {
    setup();
    service.adjustQuantity.mockReturnValue(of(mk(1, 8)));
    const p = cmp.products()[0];

    cmp.adjust(p, 1);
    cmp.adjust(p, 1);
    cmp.adjust(p, 1);

    expect(qty()).toBe(8); // otimista
    expect(service.adjustQuantity).not.toHaveBeenCalled(); // ainda no debounce

    vi.advanceTimersByTime(400);
    expect(service.adjustQuantity).toHaveBeenCalledTimes(1);
    expect(service.adjustQuantity).toHaveBeenCalledWith(1, 3);
  });

  it('adota a quantidade devolvida pelo servidor', () => {
    setup();
    service.adjustQuantity.mockReturnValue(of(mk(1, 9))); // outro usuário também alterou
    cmp.adjust(cmp.products()[0], 1);
    vi.advanceTimersByTime(400);
    expect(qty()).toBe(9);
  });

  it('não deixa passar de 0 e anuncia o limite', () => {
    setup([mk(1, 0)]);
    cmp.adjust(cmp.products()[0], -1);
    vi.advanceTimersByTime(400);
    expect(qty()).toBe(0);
    expect(service.adjustQuantity).not.toHaveBeenCalled();
    expect(cmp.announcement()).toContain('mínimo');
  });

  it('409: reverte só o delta enviado e preserva o clique posterior', () => {
    setup();
    const first = new Subject<Product>();
    service.adjustQuantity.mockReturnValueOnce(first);
    service.adjustQuantity.mockReturnValue(of(mk(1, 6))); // o clique posterior, enviado no cleanup
    const p = cmp.products()[0];

    cmp.adjust(p, 1);
    cmp.adjust(p, 1);
    vi.advanceTimersByTime(400); // envia delta +2 (UI 7)
    cmp.adjust(p, 1); // clique novo, ainda pendente (UI 8)
    expect(qty()).toBe(8);

    first.error(new HttpErrorResponse({ status: 409 }));

    expect(qty()).toBe(6); // 8 - 2: só o delta falho foi revertido
    expect(snack.open.mock.calls[0][0]).toContain('0 a 1.000.000');
  });

  it('429: mostra o Retry-After e reverte', () => {
    setup();
    service.adjustQuantity.mockReturnValue(
      new Observable((s) => s.error(new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '7' }) })))
    );
    cmp.adjust(cmp.products()[0], 1);
    vi.advanceTimersByTime(400);

    expect(qty()).toBe(5);
    expect(snack.open.mock.calls[0][0]).toContain('7 s');
  });

  it('respostas fora de ordem: a mais antiga não regride a UI', () => {
    setup();
    const a = new Subject<Product>();
    const b = new Subject<Product>();
    service.adjustQuantity.mockReturnValueOnce(a).mockReturnValueOnce(b);
    const p = cmp.products()[0];

    cmp.adjust(p, 1);
    vi.advanceTimersByTime(400); // PATCH A (+1) em voo
    cmp.adjust(p, 1);
    vi.advanceTimersByTime(400); // PATCH B (+1) em voo
    expect(qty()).toBe(7);

    b.next(mk(1, 7)); b.complete(); // B responde primeiro (estado final)
    a.next(mk(1, 6)); a.complete(); // A responde depois, com valor mais antigo

    expect(qty()).toBe(7);
  });

  it('resíduo (a): load() espera o PATCH em voo antes do GET', () => {
    setup();
    const patch = new Subject<Product>();
    service.adjustQuantity.mockReturnValue(patch);
    service.getProducts.mockClear();

    cmp.adjust(cmp.products()[0], 1);
    cmp.load(); // flushAll envia o PATCH e o GET deve aguardar

    expect(service.adjustQuantity).toHaveBeenCalledWith(1, 1);
    expect(service.getProducts).not.toHaveBeenCalled();

    service.getProducts.mockReturnValue(of([mk(1, 6)]));
    patch.next(mk(1, 6)); patch.complete();

    expect(service.getProducts).toHaveBeenCalledTimes(1);
    expect(qty()).toBe(6);
  });

  it('resíduo (b): falha do PATCH após a lista ser recarregada não subtrai em dobro', () => {
    setup([mk(1, 5)]);
    const patch = new Subject<Product>();
    const get = new Subject<Product[]>();
    service.adjustQuantity.mockReturnValue(patch);

    service.getProducts.mockReturnValue(get);
    cmp.load(); // GET em voo (antes do clique)
    cmp.products.set([mk(1, 5)]);
    cmp.adjust(cmp.products()[0], 1);
    vi.advanceTimersByTime(400); // PATCH +1 em voo (UI 6)

    get.next([mk(1, 5)]); get.complete(); // GET devolve o estado do servidor (5), sem o delta
    expect(qty()).toBe(5);

    patch.error(new HttpErrorResponse({ status: 500 }));

    expect(qty()).toBe(5); // não vira 4
  });

  it('dois load() seguidos com PATCH em voo geram um único GET', () => {
    setup();
    const patch = new Subject<Product>();
    service.adjustQuantity.mockReturnValue(patch);
    service.getProducts.mockClear();

    cmp.adjust(cmp.products()[0], 1);
    cmp.load();
    cmp.load();
    expect(service.getProducts).not.toHaveBeenCalled();

    service.getProducts.mockReturnValue(of([mk(1, 6)]));
    patch.next(mk(1, 6)); patch.complete();

    expect(service.getProducts).toHaveBeenCalledTimes(1);
  });

  it('não dispara o GET se o componente for destruído enquanto espera o PATCH', () => {
    setup();
    const patch = new Subject<Product>();
    service.adjustQuantity.mockReturnValue(patch);
    service.getProducts.mockClear();

    cmp.adjust(cmp.products()[0], 1);
    cmp.load();
    TestBed.resetTestingModule(); // destrói o componente
    patch.next(mk(1, 6)); patch.complete();

    expect(service.getProducts).not.toHaveBeenCalled();
  });

  it('envia o PATCH mesmo se o item sair da lista durante o debounce (filtro)', () => {
    setup();
    service.adjustQuantity.mockReturnValue(of(mk(1, 6)));
    cmp.adjust(cmp.products()[0], 1);
    cmp.products.set([]); // lista trocada antes dos 400ms

    vi.advanceTimersByTime(400);

    expect(service.adjustQuantity).toHaveBeenCalledWith(1, 1);
  });
});
