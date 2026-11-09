/**
 * Estilos compartilhados pelos diálogos de usuário.
 *
 * REGRA: um componente por arquivo .ts quando ele tem `styles` inline. Dois componentes com estilos inline
 * no MESMO arquivo deixam um contexto do esbuild aberto e `ng build`/`ng test` não encerram o processo
 * (travou o CI). Compartilhe o CSS por constante importada, como aqui.
 */
export const DIALOG_STYLES = `
  :host { display: block; color: var(--sm-text); }
  h2 { font-size: 1.25rem; font-weight: 600; }
  .sub { margin: 0 0 16px; color: var(--sm-text-2); }
  .field { display: flex; flex-direction: column; gap: 6px; margin-bottom: 16px; }
  label { font-size: 0.875rem; font-weight: 500; }
  input, select { width: 100%; box-sizing: border-box; height: 52px; padding: 0 14px; border: 1px solid var(--sm-control-border); border-radius: var(--sm-radius); background: var(--sm-surface-2); color: var(--sm-text); font: inherit; }
  input:focus-visible, select:focus-visible { outline: 2px solid var(--sm-primary); outline-offset: 1px; background: var(--sm-surface); }
  input[aria-invalid='true'] { border-color: var(--sm-danger); }
  .hint { color: var(--sm-text-2); font-size: 0.8125rem; }
  .alert { display: flex; gap: 10px; padding: 12px 14px; margin-bottom: 16px; border-radius: var(--sm-radius); background: var(--sm-danger-soft); color: var(--sm-danger); font-size: 0.9375rem; }
  mat-dialog-actions { gap: 8px; padding: 8px 24px 20px; }
  .btn { display: inline-flex; align-items: center; justify-content: center; min-height: 48px; padding: 0 20px; border-radius: var(--sm-radius); font: inherit; font-weight: 600; cursor: pointer; }
  .btn-outline { border: 1px solid var(--sm-control-border); background: transparent; color: var(--sm-text); }
  .btn-primary { border: 1px solid var(--sm-primary); background: var(--sm-primary); color: var(--sm-on-primary); }
  .btn-primary:disabled { opacity: 0.6; cursor: progress; }
  @media (max-width: 480px) { mat-dialog-actions { flex-direction: column-reverse; align-items: stretch; } }
`;
