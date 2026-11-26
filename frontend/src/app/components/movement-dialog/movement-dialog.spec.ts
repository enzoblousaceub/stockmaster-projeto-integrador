import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Observable, of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementDialogComponent } from './movement-dialog';
import { ProductService } from '../../services/product.service';

describe('MovementDialogComponent', () => {
  let service: { addMovement: ReturnType<typeof vi.fn> };
  let ref: { close: ReturnType<typeof vi.fn> };

  function create(type?: 'Entrada' | 'Saida' | 'Ajuste') {
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        { provide: ProductService, useValue: service },
        { provide: MatDialogRef, useValue: ref },
        { provide: MAT_DIALOG_DATA, useValue: { product: { id: 7, name: 'Mouse', quantity: 5 }, type } }
      ]
    });
    const f = TestBed.createComponent(MovementDialogComponent);
    f.detectChanges();
    return f.componentInstance;
  }

  beforeEach(() => {
    service = { addMovement: vi.fn() };
    ref = { close: vi.fn() };
  });

  it('saída padrão: envia tipo, quantidade, motivo e data ISO', () => {
    const cmp = create();
    const result = { movement: { id: 1 }, product: { id: 7, quantity: 3 } };
    service.addMovement.mockReturnValue(of(result));
    cmp.quantity.set(2);
    cmp.submit();

    expect(service.addMovement).toHaveBeenCalledTimes(1);
    const [id, body] = service.addMovement.mock.calls[0];
    expect(id).toBe(7);
    expect(body.type).toBe('Saida');
    expect(body.quantity).toBe(2);
    expect(body.reason).toBe('Venda');
    expect(new Date(body.occurredAt).toISOString()).toBe(body.occurredAt);
    expect(ref.close).toHaveBeenCalledWith(result);
  });

  it('não deixa sair mais do que o saldo (sem chamar a API)', () => {
    const cmp = create('Saida');
    cmp.quantity.set(6);
    cmp.submit();
    expect(service.addMovement).not.toHaveBeenCalled();
    expect(cmp.error()).toContain('Estoque insuficiente');
  });

  it('quantidade zero na entrada é inválida; na conferência é permitida', () => {
    const entrada = create('Entrada');
    entrada.quantity.set(0);
    expect(entrada.validate()?.field).toBe('quantity');
    entrada.setType('Ajuste');
    expect(entrada.validate()).toBeNull();
  });

  it('motivo "Outro" na saída exige descrição', () => {
    const cmp = create('Saida');
    cmp.quantity.set(1);
    cmp.reasonOption.set('Outro');
    expect(cmp.validate()?.field).toBe('reason');
    cmp.reasonOther.set('Brinde para cliente');
    expect(cmp.validate()).toBeNull();
  });

  it('data no futuro é recusada', () => {
    const cmp = create('Entrada');
    cmp.quantity.set(1);
    cmp.date.set('2999-01-01T10:00');
    expect(cmp.validate()?.field).toBe('occurredAt');
  });

  it('mostra a mensagem do servidor (estoque insuficiente) e mantém o diálogo aberto', () => {
    const cmp = create('Saida');
    service.addMovement.mockReturnValue(new Observable((s) => s.error(new HttpErrorResponse({ status: 409, error: { code: 'insufficient_stock' } }))));
    cmp.quantity.set(1);
    cmp.submit();
    expect(ref.close).not.toHaveBeenCalled();
    expect(cmp.error()).toContain('Estoque insuficiente');
    expect(cmp.loading()).toBe(false);
  });

  it('prévia do saldo após a movimentação', () => {
    const cmp = create('Entrada');
    cmp.quantity.set(10);
    expect(cmp.preview()).toContain('15');
    cmp.setType('Ajuste');
    expect(cmp.preview()).toContain('10');
  });
});
