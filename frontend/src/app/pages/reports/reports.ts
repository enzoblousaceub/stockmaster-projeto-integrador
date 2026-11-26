import { Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MatIconModule } from '@angular/material/icon';
import { ProductService } from '../../services/product.service';
import { MOVEMENT_LABEL, Movement, MovementType, ReportSummary } from '../../models/product.model';

/** Relatórios de movimentação e previsão de ruptura/reposição (RF010/RF011). */
@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [RouterLink, MatIconModule, CurrencyPipe, DatePipe, DecimalPipe],
  templateUrl: './reports.html',
  styleUrl: './reports.scss'
})
export class ReportsComponent {
  private products = inject(ProductService);

  readonly periods = [7, 30, 90];
  readonly days = signal(30);
  readonly state = signal<'loading' | 'error' | 'ready'>('loading');
  readonly report = signal<ReportSummary | null>(null);
  readonly recent = signal<Movement[]>([]);
  readonly label = MOVEMENT_LABEL;

  /** Itens que precisam de compra, os que acabam antes primeiro. */
  readonly reorder = computed(() => (this.report()?.forecast ?? []).filter((f) => f.needsReorder));
  /** Itens com consumo e sem urgência (para mostrar a previsão de todos). */
  readonly watch = computed(() => (this.report()?.forecast ?? []).filter((f) => !f.needsReorder && f.avgDailyOut > 0));
  readonly reorderCost = computed(() => this.reorder().reduce((s, f) => s + f.suggestedReorder * f.cost, 0));
  /** Barras do gráfico diário (altura relativa ao maior valor do período). */
  readonly bars = computed(() => {
    const daily = this.report()?.daily ?? [];
    const max = Math.max(1, ...daily.map((d) => Math.max(d.in, d.out)));
    return daily.map((d) => ({ ...d, inPct: (d.in / max) * 100, outPct: (d.out / max) * 100 }));
  });

  constructor() {
    this.load();
  }

  setDays(d: number): void {
    if (d === this.days()) return;
    this.days.set(d);
    this.load();
  }

  load(): void {
    this.state.set('loading');
    forkJoin({ report: this.products.getReport(this.days()), recent: this.products.getMovements({ limit: 15 }) }).subscribe({
      next: ({ report, recent }) => {
        this.report.set(report);
        this.recent.set(recent);
        this.state.set('ready');
      },
      error: () => this.state.set('error')
    });
  }

  chipClass(type: MovementType): string {
    return type === 'Entrada' ? 'chip chip--in' : type === 'Saida' ? 'chip chip--out' : 'chip chip--adj';
  }

  /** Data (yyyy-MM-dd) sem conversão de fuso: o servidor já agrupou no horário de Brasília. */
  dayLabel(iso: string): string {
    const [, m, d] = iso.split('-');
    return `${d}/${m}`;
  }
}
