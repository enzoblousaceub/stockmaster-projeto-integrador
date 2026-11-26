export interface Product {
  id: number;
  name: string;
  description?: string;
  category: string;
  quantity: number;
  price: number;
  /** custo por unidade */
  cost: number;
  minStock: number;
  createdAt: string;
  updatedAt: string;
  /** null = sem imagem; muda a cada troca (usado para invalidar o cache da imagem) */
  imageUpdatedAt?: string | null;
}

export interface CreateProduct {
  name: string;
  description?: string;
  category: string;
  quantity: number;
  price: number;
  cost: number;
  minStock: number;
}

export type UpdateProduct = CreateProduct;

export interface DashboardStats {
  totalProducts: number;
  totalItems: number;
  totalValue: number;
  totalCost: number;
  lowStockCount: number;
  outOfStockCount: number;
  categories: number;
}

export type MovementType = 'Entrada' | 'Saida' | 'Ajuste';

export interface Movement {
  id: number;
  productId: number | null;
  productName: string;
  type: MovementType;
  /** variação no saldo (+ entrada, - saída) */
  delta: number;
  quantityAfter: number;
  reason: string | null;
  occurredAt: string;
  createdAt: string;
  createdBy: string | null;
}

/** Entrada/Saida: quantity = unidades. Ajuste: quantity = saldo contado. */
export interface NewMovement {
  type: MovementType;
  quantity: number;
  reason?: string | null;
  occurredAt?: string;
}

export interface MovementResult {
  movement: Movement;
  product: Product;
}

export interface ProductForecast {
  productId: number;
  name: string;
  category: string;
  quantity: number;
  minStock: number;
  price: number;
  cost: number;
  outQuantity: number;
  inQuantity: number;
  avgDailyOut: number;
  daysToStockout: number | null;
  stockoutDate: string | null;
  suggestedReorder: number;
  needsReorder: boolean;
}

export interface ReportSummary {
  days: number;
  from: string;
  to: string;
  inQuantity: number;
  outQuantity: number;
  adjustments: number;
  movementCount: number;
  stockAtCost: number;
  stockAtPrice: number;
  potentialMargin: number;
  revenueEstimate: number;
  marginEstimate: number;
  daily: { date: string; in: number; out: number }[];
  forecast: ProductForecast[];
  topSellers: { productId: number; name: string; category: string; outQuantity: number; revenue: number; margin: number }[];
  stale: { productId: number; name: string; category: string; quantity: number; valueAtCost: number; lastOutAt: string | null }[];
}

/** Rótulo exibido para o tipo (a API usa "Saida" sem acento). */
export const MOVEMENT_LABEL: Record<MovementType, string> = { Entrada: 'Entrada', Saida: 'Saída', Ajuste: 'Ajuste' };

/** Código curto do produto (RF002), o mesmo da auditoria. */
export function productCode(id: number): string {
  return '#' + String(id).padStart(4, '0');
}

/** URL da imagem com versão (o navegador pode guardar em cache até a próxima troca). */
export function productImageUrl(p: Pick<Product, 'id' | 'imageUpdatedAt'>): string | null {
  return p.imageUpdatedAt ? `/api/products/${p.id}/image?v=${encodeURIComponent(p.imageUpdatedAt)}` : null;
}
