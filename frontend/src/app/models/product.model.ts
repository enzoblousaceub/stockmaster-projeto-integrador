/** Uma combinação da grade (tamanho x material) com saldo próprio. */
export interface Variant {
  id: number;
  productId: number;
  size: string;
  /** vazio quando o produto não varia por material */
  material: string;
  quantity: number;
  minStock: number;
  sortOrder: number;
  label: string;
}

export interface Product {
  id: number;
  name: string;
  description?: string;
  category: string;
  brand?: string | null;
  /** soma dos saldos das variações */
  quantity: number;
  price: number;
  /** custo por unidade */
  cost: number;
  /** soma dos mínimos das variações */
  minStock: number;
  variants: Variant[];
  createdAt: string;
  updatedAt: string;
  /** null = sem imagem; muda a cada troca (usado para invalidar o cache da imagem) */
  imageUpdatedAt?: string | null;
}

export interface VariantInput {
  id?: number;
  size: string;
  material: string;
  quantity: number;
  minStock: number;
}

export interface CreateProduct {
  name: string;
  description?: string;
  category: string;
  brand?: string | null;
  price: number;
  cost: number;
  variants: VariantInput[];
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
  variantId: number | null;
  variantLabel: string | null;
  saleId: number | null;
  paymentMethod: PaymentMethod | null;
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
  variantId?: number;
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

// ---------- grade ----------

export type StockLevel = 'ok' | 'low' | 'out';

export function levelOf(v: Pick<Variant, 'quantity' | 'minStock'>): StockLevel {
  if (v.quantity <= 0) return 'out';
  return v.quantity <= v.minStock ? 'low' : 'ok';
}

/** Materiais da grade na ordem de cadastro ('' = sem material). */
export function materialsOf(p: Pick<Product, 'variants'>): string[] {
  return [...new Set(p.variants.map((v) => v.material))];
}

/** Produto sem grade de verdade (só a variação "Único"). */
export function isSingle(p: Pick<Product, 'variants'>): boolean {
  return p.variants.length === 1 && p.variants[0].size === 'Único' && !p.variants[0].material;
}

/** "40, Jeans" para leitura (o rótulo da API usa ponto médio). */
export function variantText(label: string | null | undefined): string {
  return (label ?? '').replace(' · ', ', ');
}

/** Duas letras para o quadrado sem foto. */
export function monogram(name: string): string {
  const words = name.trim().split(/\s+/).filter((w) => w.length > 2 || /^[A-Z]/.test(w));
  return ((words[0]?.[0] ?? '') + (words[1]?.[0] ?? '')).toUpperCase() || '?';
}

export interface StockAlert {
  productId: number;
  productName: string;
  brand: string | null;
  category: string;
  imageUpdatedAt: string | null;
  variantId: number;
  variantLabel: string;
  quantity: number;
  minStock: number;
}

// ---------- vendas ----------

export type PaymentMethod = 'Dinheiro' | 'Pix' | 'Debito' | 'Credito';

export const PAYMENT_METHODS: { value: PaymentMethod; label: string; icon: string }[] = [
  { value: 'Pix', label: 'Pix', icon: 'pix' },
  { value: 'Dinheiro', label: 'Dinheiro', icon: 'payments' },
  { value: 'Debito', label: 'Débito', icon: 'credit_card' },
  { value: 'Credito', label: 'Crédito', icon: 'credit_score' }
];

export const PAYMENT_LABEL: Record<PaymentMethod, string> = { Pix: 'Pix', Dinheiro: 'Dinheiro', Debito: 'Débito', Credito: 'Crédito' };

export interface SaleItem {
  productId: number | null;
  productName: string;
  variantId: number | null;
  variantLabel: string | null;
  quantity: number;
}

export interface Sale {
  id: number;
  paymentMethod: PaymentMethod;
  createdAt: string;
  createdBy: string | null;
  undoneAt: string | null;
  undoneBy: string | null;
  undoDeadline: string;
  canUndo: boolean;
  totalItems: number;
  items: SaleItem[];
}

export interface NewSale {
  paymentMethod: PaymentMethod;
  items: { variantId: number; quantity: number }[];
}
