import { Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ProductService } from '../../services/product.service';
import { MovementResult, MovementType, Product } from '../../models/product.model';
import { messageFor, parseProblem } from '../../core/api-error';
import { DIALOG_STYLES } from '../user-dialogs/dialog-styles';

export interface MovementDialogData {
  product: Pick<Product, 'id' | 'name' | 'quantity'>;
  /** tipo pré-selecionado */
  type?: MovementType;
}

const REASONS: Record<MovementType, string[]> = {
  Entrada: ['Compra de fornecedor', 'Devolução de cliente', 'Produção própria', 'Outro'],
  Saida: ['Venda', 'Perda/avaria', 'Uso interno', 'Devolução ao fornecedor', 'Outro'],
  Ajuste: ['Conferência de estoque', 'Inventário mensal', 'Outro']
};

/** Data/hora local no formato do <input type="datetime-local"> (yyyy-MM-ddTHH:mm). */
function localInputValue(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Registro de entrada (RF006), saída (RF007) ou conferência de um produto. Fecha com o resultado da API. */
@Component({
  selector: 'app-movement-dialog',
  standalone: true,
  imports: [MatDialogModule, MatIconModule, DecimalPipe],
  template: `
    <h2 mat-dialog-title id="movement-title">Movimentar estoque</h2>
    <form (submit)="$event.preventDefault(); submit()" novalidate>
      <mat-dialog-content>
        <p class="sub"><b>{{ data.product.name }}</b> · saldo atual <b class="num">{{ data.product.quantity | number }}</b></p>
        @if (error()) { <div class="alert" role="alert">{{ error() }}</div> }

        <div class="types" role="radiogroup" aria-label="Tipo de movimentação">
          @for (t of types; track t.value) {
            <button type="button" role="radio" class="type" [class.on]="type() === t.value" [attr.aria-checked]="type() === t.value"
              (click)="setType(t.value)" [id]="'mv-type-' + t.value">
              <mat-icon aria-hidden="true">{{ t.icon }}</mat-icon><span>{{ t.label }}</span>
            </button>
          }
        </div>

        <div class="field">
          <label for="mv-qty">{{ type() === 'Ajuste' ? 'Saldo contado' : 'Quantidade' }}</label>
          <input #q id="mv-qty" type="number" inputmode="numeric" min="0" max="1000000" step="1" required
            [value]="quantity() ?? ''" (input)="quantity.set(q.value === '' ? null : +q.value)"
            [attr.aria-invalid]="fieldError() === 'quantity' ? 'true' : null" aria-describedby="mv-qty-hint">
          <span class="hint" id="mv-qty-hint">{{ preview() }}</span>
        </div>

        <div class="field">
          <label for="mv-reason">Motivo{{ type() === 'Saida' ? '' : ' (opcional)' }}</label>
          <select #r id="mv-reason" (change)="reasonOption.set(r.value)" [attr.aria-invalid]="fieldError() === 'reason' ? 'true' : null">
            @for (o of reasons(); track o) { <option [value]="o" [selected]="o === reasonOption()">{{ o }}</option> }
          </select>
        </div>
        @if (reasonOption() === 'Outro') {
          <div class="field">
            <label for="mv-reason-other">Descreva o motivo</label>
            <input #o id="mv-reason-other" type="text" maxlength="200" [value]="reasonOther()" (input)="reasonOther.set(o.value)">
          </div>
        }

        <div class="field">
          <label for="mv-date">Data</label>
          <input #d id="mv-date" type="datetime-local" [max]="maxDate" [value]="date()" (input)="date.set(d.value)"
            [attr.aria-invalid]="fieldError() === 'occurredAt' ? 'true' : null">
        </div>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button type="button" class="btn btn-outline" mat-dialog-close>Cancelar</button>
        <button type="submit" class="btn btn-primary" [disabled]="loading()" id="btn-save-movement">{{ loading() ? 'Salvando…' : 'Registrar' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: [DIALOG_STYLES, `
    .types { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; margin-bottom: 16px; }
    .type { display: flex; flex-direction: column; align-items: center; gap: 4px; min-height: 64px; padding: 8px; border: 1px solid var(--sm-control-border);
      border-radius: var(--sm-radius); background: transparent; color: var(--sm-text); font: inherit; font-weight: 600; cursor: pointer; }
    .type.on { border-color: var(--sm-primary); background: var(--sm-primary-soft); color: var(--sm-primary); }
    .num { font-family: var(--sm-font-mono); }
  `]
})
export class MovementDialogComponent {
  readonly data = inject<MovementDialogData>(MAT_DIALOG_DATA);
  private ref = inject<MatDialogRef<MovementDialogComponent, MovementResult>>(MatDialogRef);
  private products = inject(ProductService);

  readonly types: { value: MovementType; label: string; icon: string }[] = [
    { value: 'Entrada', label: 'Entrada', icon: 'south_west' },
    { value: 'Saida', label: 'Saída', icon: 'north_east' },
    { value: 'Ajuste', label: 'Conferência', icon: 'fact_check' }
  ];
  readonly maxDate = localInputValue(new Date(Date.now() + 60_000));

  readonly type = signal<MovementType>(this.data.type ?? 'Saida');
  readonly quantity = signal<number | null>(null);
  readonly reasonOption = signal(REASONS[this.type()][0]);
  readonly reasonOther = signal('');
  readonly date = signal(localInputValue(new Date()));
  readonly loading = signal(false);
  readonly error = signal('');
  readonly fieldError = signal<string | null>(null);

  readonly reasons = computed(() => REASONS[this.type()]);
  readonly preview = computed(() => {
    const q = this.quantity();
    const cur = this.data.product.quantity;
    if (q === null || !Number.isInteger(q) || q < 0) return this.type() === 'Ajuste' ? 'Informe quantas unidades foram contadas.' : 'Informe um número inteiro.';
    const after = this.type() === 'Entrada' ? cur + q : this.type() === 'Saida' ? cur - q : q;
    return `Saldo após: ${after.toLocaleString('pt-BR')}`;
  });

  setType(t: MovementType): void {
    this.type.set(t);
    this.reasonOption.set(REASONS[t][0]);
    this.error.set('');
    this.fieldError.set(null);
  }

  /** Retorna a mensagem de erro de validação local, ou null. */
  validate(): { field: string; message: string } | null {
    const q = this.quantity();
    const t = this.type();
    if (q === null || !Number.isInteger(q) || q < (t === 'Ajuste' ? 0 : 1) || q > 1_000_000)
      return { field: 'quantity', message: t === 'Ajuste' ? 'Informe o saldo contado (0 a 1.000.000).' : 'Informe uma quantidade inteira maior que zero.' };
    if (t === 'Saida' && q > this.data.product.quantity)
      return { field: 'quantity', message: `Estoque insuficiente: o saldo atual é ${this.data.product.quantity}.` };
    if (this.reasonOption() === 'Outro' && t === 'Saida' && !this.reasonOther().trim())
      return { field: 'reason', message: 'Descreva o motivo da saída.' };
    const when = new Date(this.date());
    if (!this.date() || Number.isNaN(when.getTime())) return { field: 'occurredAt', message: 'Informe a data.' };
    if (when.getTime() > Date.now() + 5 * 60_000) return { field: 'occurredAt', message: 'A data não pode estar no futuro.' };
    return null;
  }

  submit(): void {
    if (this.loading()) return;
    const invalid = this.validate();
    if (invalid) {
      this.error.set(invalid.message);
      this.fieldError.set(invalid.field);
      return;
    }
    const reason = this.reasonOption() === 'Outro' ? this.reasonOther().trim() || null : this.reasonOption();
    this.error.set('');
    this.fieldError.set(null);
    this.loading.set(true);
    this.products.addMovement(this.data.product.id, {
      type: this.type(),
      quantity: this.quantity()!,
      reason,
      occurredAt: new Date(this.date()).toISOString()
    }).subscribe({
      next: (result) => this.ref.close(result),
      error: (err) => {
        this.loading.set(false);
        const p = parseProblem(err);
        this.error.set(p.status === 0 || p.status === -1 ? 'Sem conexão com o servidor. Tente novamente.' : messageFor(p));
      }
    });
  }
}
