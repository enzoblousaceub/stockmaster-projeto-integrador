import { Perfil } from '../../models/auth.model';

/** Opções de perfil dos diálogos de usuário. */
export const PERFIS: { value: Perfil; label: string }[] = [
  { value: 'Visualizador', label: 'Visualizador (somente leitura)' },
  { value: 'Admin', label: 'Admin (acesso total)' }
];
