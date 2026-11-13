# Requisitos do Assistente de Estoque com IA (linguagem natural)

> Status: **levantamento de requisitos** (nada implementado). Complementa `docs/features-ia.md`, funcionalidade 1.
> Todas as decisões abaixo foram tomadas em sessão de entrevista com o responsável pelo projeto.

## 1. Visão geral

Assistente em linguagem natural (português do Brasil) para o Stock Master. O usuário conversa para consultar o estoque; o Admin também pode pedir ajustes de quantidade, sempre com confirmação explícita.

Contexto técnico existente: ASP.NET Core (.NET 10), EF Core, PostgreSQL, Angular (PWA). Leituras de produtos (`GET /api/products`, `/stats`, `/categories`, `/{id}`) disponíveis a qualquer usuário autenticado; escritas restritas à política Admin; rate limiting e `AuditLog` já existentes.

## 2. Requisitos funcionais

| # | Requisito |
|---|-----------|
| RF1 | **Visualizador:** o assistente só consulta (somente leitura). |
| RF2 | **Admin:** consulta e pode pedir **ajuste de quantidade** (entrada ou saída). Nenhuma outra ação: criar, editar e excluir produtos ficam fora do escopo do assistente. |
| RF3 | O modelo **apenas propõe** o ajuste. Um cartão mostra o estado atual lido do banco, com botões **Confirmar** e **Cancelar**. A confirmação chama o endpoint de ajuste já existente (política Admin e `AuditLog`). |
| RF4 | Conversa com várias trocas. O histórico existe só na sessão do navegador e não é salvo no servidor. |
| RF5 | Restrito ao domínio de estoque. Quando o dado não existe (ex.: não há histórico de movimentações), informa isso claramente e **nunca inventa números**. Assuntos fora do domínio recebem recusa educada e curta, sem sugestões. |
| RF6 | Página dedicada `/assistente`, com o componente de chat isolado para, no futuro, virar painel flutuante. |
| RF7 | Respostas por **streaming (SSE)**, exibindo o estado da operação (ex.: "consultando estoque…"). |
| RF8 | Poucas ferramentas de leitura com **filtros parametrizados** (categoria, faixa de preço, abaixo do mínimo, ordenação e limite). Começa com 4 ou 5 ferramentas. |
| RF9 | Busca de produtos **semântica** (embeddings). A funcionalidade 2 (busca semântica) é, portanto, pré-requisito do assistente. |

## 3. Requisitos não funcionais

| # | Categoria | Requisito |
|---|-----------|-----------|
| RNF1 | Segurança | O modelo não possui nenhuma ferramenta de escrita. O papel do usuário vem do token de login, nunca do conteúdo do chat. |
| RNF2 | Segurança | Dados de produtos voltam ao modelo como **dados**, não como instruções (mitigação de prompt injection). |
| RNF3 | Precisão | Somas, contagens e valores são calculados pelo banco, nunca pelo modelo. |
| RNF4 | Privacidade | Dados pessoais (ex.: e-mail dos usuários) nunca são enviados ao modelo. Apenas dados de estoque saem para provedores externos. |
| RNF5 | Provedores | **Claude Haiku 5.5** (chat) e **Voyage AI** (embeddings). Chaves apenas em variáveis de ambiente, nunca no git. A API é cobrada por token, separada da assinatura do Claude Code. |
| RNF6 | Custo | Limite por usuário (perguntas por hora e teto diário), pergunta de no máximo ~500 caracteres, histórico enviado limitado às últimas N trocas, `max_tokens` fixo e limite mensal de gastos no Console. |
| RNF7 | Disponibilidade | Assistente isolado do resto do sistema. Timeout de ~30 s, uma nova tentativa em erros temporários, mensagem amigável em português. Sem chave configurada, o item de menu fica oculto ou desativado. |
| RNF8 | Telemetria | `AuditLog` com quem usou, quando e quais ferramentas foram chamadas, mais tokens, latência, modelo, erros e se a proposta foi confirmada ou cancelada. **Não** guarda o texto das conversas. |
| RNF9 | Qualidade | Testes automatizados da parte determinística (LLM simulado); conjunto de avaliação de 20 a 30 perguntas com o modelo real, sob demanda; casos de segurança (prompt injection, Visualizador forçando uma ação, assuntos fora do domínio, pedido de dados de usuários). |
| RNF10 | Infraestrutura | pgvector no Docker local e no Postgres do Render (confirmar suporte antes de começar). Proxy sem buffer para o SSE. |
| RNF11 | Idioma | Respostas sempre em português do Brasil, com tom objetivo. |

## 4. Fora de escopo

- Criar, editar ou excluir produtos pelo assistente.
- Histórico de conversas salvo no servidor.
- Conversa sobre assuntos fora de estoque.
- Painel flutuante em todas as telas (previsto para evolução futura).
- Previsão de reposição (funcionalidade 4) e resumo por e-mail (funcionalidade 5).

## 5. Decisões registradas

| # | Decisão | Escolha |
|---|---------|---------|
| 1 | O que o assistente pode fazer | Visualizador: leitura. Admin: leitura + ações com confirmação |
| 2 | Ações do Admin | Somente ajuste de quantidade |
| 3 | Conversa e histórico | Várias trocas, histórico só na sessão |
| 4 | Perguntas fora do alcance | Restrito ao estoque, sem sugestões |
| 5 | Local no frontend | Página dedicada |
| 6 | Entrega da resposta | Streaming (SSE) |
| 7 | Provedor de LLM | Claude Haiku 5.5 |
| 8 | Controle de custo | Limite por usuário + limites de tamanho + limite no Console |
| 9 | Confirmação de ações | Modelo propõe; cartão com Confirmar/Cancelar executa |
| 10 | Acesso aos dados | Poucas ferramentas com filtros parametrizados |
| 11 | Garantia de qualidade | Testes + conjunto de avaliação + casos de segurança |
| 12 | Falha do provedor | Isolamento, timeout, uma nova tentativa, mensagem amigável |
| 13 | Busca de produtos | Semântica (embeddings) desde a primeira versão |
| 14 | Provedor de embeddings | Voyage AI |
| 15 | Telemetria | Métricas por chamada, sem guardar texto das conversas |

## 6. Dependências e ordem sugerida

1. Infra de IA: pgvector, serviço de embeddings (Voyage), reindexação ao criar/editar produto (funcionalidade 2).
2. Camada de chat (`IChatService`) com Claude Haiku 5.5, ferramentas de leitura e streaming.
3. Cartão de confirmação do ajuste de quantidade (Admin).
4. Limites de custo, telemetria e tratamento de falhas.
5. Testes e conjunto de avaliação, incluindo casos de segurança.
