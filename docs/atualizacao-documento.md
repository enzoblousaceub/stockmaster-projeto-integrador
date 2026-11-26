# Sugestões de atualização do documento "Estoque Master"

Texto sugerido para alinhar o documento do Projeto Integrador ao que está implementado (branch `feat/movimentacoes`).

## 6. Recursos-Chave (Business Model Canvas)
Substituir a stack:
- Backend: C#/.NET 10 (ASP.NET Core), Entity Framework Core
- Frontend: Angular 21 / PWA (Angular Material)
- Banco de Dados: PostgreSQL 17
- Hospedagem: Render (Docker) e CI no GitHub Actions

## 7.1 EAP
- 1.4.2 Frontend (Angular/PWA)
- 1.4.3 Integração com banco (PostgreSQL)

## 9.1 Requisitos funcionais: situação
| Requisito | Situação |
|---|---|
| RF001 Cadastro (nome, descrição, categoria, custo) | Implementado (também preço de venda e estoque mínimo) |
| RF002 Identificação única automática | Implementado (código `#0001`) |
| RF003 Upload de imagens | Implementado (1 imagem por produto, JPEG/PNG/WebP até 2 MB) |
| RF004 Validação de campos | Implementado (front e back) |
| RF005 Edição e exclusão | Implementado (com confirmação e auditoria) |
| RF006 Entrada com quantidade e data | Implementado |
| RF007 Saída com quantidade e motivo | Implementado (motivo obrigatório; não permite saldo negativo) |
| RF008 Atualização automática do saldo | Implementado (saldo = soma do histórico) |
| RF009 Estoque atual em tempo real | Implementado |
| RF010 Previsão de ruptura | Implementado (média de saídas dos últimos 30 dias) |
| RF011 Recomendação de compra | Implementado (consumo de 30 dias + mínimo − saldo) |
| RF012 Reconhecimento por imagem | Futuro |
| RF013 Chatbot | Futuro (planejado em `docs/features-ia.md`) |

Requisitos adicionais implementados (sugestão de inclusão):
- **RF014 – Perfis de acesso:** Admin (acesso total), Operador (registra entradas/saídas) e Visualizador (somente leitura).
- **RF015 – Conferência de estoque:** o usuário informa o saldo contado e o sistema registra a diferença como ajuste.
- **RF016 – Relatórios de movimentação:** entradas/saídas por período, mais vendidos, produtos parados, valor do estoque a custo e a preço, margem estimada.
- **RF017 – Auditoria:** registro de quem criou, editou ou excluiu produtos e de ações em usuários.

## 9.2 Requisitos não funcionais
- **RNF002 – Responsividade:** o texto atual repete o do RNF003. Sugestão: "A interface deve se adaptar a celulares, tablets e computadores, com navegação inferior no celular e áreas de toque de no mínimo 48 px."
- Sugestão de inclusão: **RNF005 – Segurança:** autenticação por cookie HttpOnly, proteção CSRF, limite de tentativas e senhas com PBKDF2.

## 11. MVP
O texto pode ser mantido; agora ele corresponde ao sistema (entradas e saídas com histórico, imagens e PWA).

## 13. Considerações finais: prioridades
Mover para "concluído": alertas de estoque mínimo, filtros e busca, relatórios de movimentação, predição de ruptura (RF010) e recomendação de compra (RF011).
