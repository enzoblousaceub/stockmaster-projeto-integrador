import { Perfil } from '../../models/auth.model';

/** Opções de perfil dos diálogos de usuário (nomes do dia a dia da loja). */
export const PERFIS: { value: Perfil; label: string }[] = [
  { value: 'Operador', label: 'Vendedor (só registra vendas)' },
  { value: 'Visualizador', label: 'Consulta (vê estoque e relatórios, não altera nada)' },
  { value: 'Admin', label: 'Dono (acesso total)' }
];

/** Nome curto do perfil para listas e selos. */
export const PERFIL_NOME: Record<Perfil, string> = { Admin: 'Dono', Operador: 'Vendedor', Visualizador: 'Consulta' };
