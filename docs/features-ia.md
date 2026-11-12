# Funcionalidades de IA — Stock Master

> Status: **planejamento** (nada implementado). Documento criado a partir da conversa de definição de escopo.
> Objetivo: funcionalidades **realmente úteis ao usuário final** do controle de estoque, não uma vitrine de técnicas.

## Contexto do sistema

- Backend: ASP.NET Core (.NET 10), EF Core, PostgreSQL 17 (Docker local; Render em produção).
- Frontend: Angular.
- Modelo atual: `Product` (nome, descrição, categoria, quantidade, preço, estoque mínimo); `AppUser` com papéis **Admin** e **Visualizador**; log de auditoria.
- E-mail transacional via Resend (ver `docs/pendencias-resender.MD`).
- Fotos de produtos foram removidas recentemente.

## Funcionalidades escolhidas

| # | Funcionalidade | Técnica principal | Fase |
|---|----------------|-------------------|------|
| 1 | Assistente de estoque em linguagem natural | LLM + tool calling | 1 |
| 2 | Busca semântica de produtos | Embeddings + pgvector | 1 |
| 3 | Cadastro assistido (sugere categoria, descrição, estoque mínimo) | Embeddings (vizinhos) + LLM | 1 |
| 4 | Previsão de reposição | Estatística simples (média móvel) | a definir |
| 5 | Resumo diário/semanal por e-mail | LLM + Resend | 2 |
| 6 | Detecção de produtos duplicados | Embeddings (similaridade) | 1 |

### 1. Assistente de estoque
O usuário pergunta em português ("o que está abaixo do mínimo?", "quanto vale meu estoque de eletrônicos?"). O LLM chama **ferramentas somente leitura** (endpoints/consultas seguras) e responde com base nos dados reais.
- Dados são estruturados, então é **tool calling, não RAG**.
- Ferramentas somem do alcance de escrita: o assistente só lê.

### 2. Busca semântica
Embedding de cada produto (nome + descrição + categoria) guardado em coluna `vector` (pgvector). A busca encontra "cabo para carregar celular" → "Cabo USB-C 1m".
- Reindexar o produto ao criar/editar.
- Reaproveitada por 3 e 6.

### 3. Cadastro assistido
Ao digitar o nome do produto, sugere categoria, descrição e estoque mínimo a partir dos produtos mais parecidos (vizinhos por embedding) e do LLM. A sugestão é **editável**; nunca grava sozinha.

### 4. Previsão de reposição
"Este item acaba em ~9 dias; reponha X unidades."
- **Pré-requisito:** hoje só existe a quantidade final. É preciso criar uma tabela de **movimentações** (entrada/saída, data, quantidade, usuário) e passar a registrá-las.
- Usa média móvel/consumo diário, sem LLM. O LLM, se entrar, só explica o resultado.

### 5. Resumo por e-mail (Fase 2)
Resumo periódico de alertas (itens críticos, mudanças relevantes) escrito pelo LLM e enviado pelo Resend. Depende do job agendado e da configuração do Resend estar concluída.

### 6. Detecção de duplicados
Avisa quando "Mouse Logitech M90" e "Mouse M90 Logitech" parecem o mesmo produto, no cadastro e/ou em uma varredura do catálogo (limiar de similaridade configurável).

## O que ficou de fora (e por quê)

- **CNN:** só se justifica com imagens; as fotos foram removidas. Se voltar a haver foto, preferir um LLM de visão pronto a treinar uma CNN.
- **Fine-tuning:** não há dados nem problema que o justifiquem. Prompt + tool calling resolvem com menos custo.
- **RAG com chunking:** os dados estão em tabelas, não em documentos. Só entraria se surgirem textos longos (manuais, fichas técnicas, políticas).

## Arquitetura prevista

- Camada `Ai/` no backend com duas abstrações: `IEmbeddingService` e `IChatService` (trocar de provedor não afeta o resto).
- PostgreSQL com extensão **pgvector**. Local: trocar a imagem do `docker-compose` por uma com pgvector. Produção: confirmar suporte no plano do Render antes de começar.
- Migration EF Core para a coluna `vector` e, na funcionalidade 4, a tabela de movimentações.
- Chaves de API somente em variáveis de ambiente (nunca no git), como o restante da configuração.
- Auditoria: registrar no `AuditLog` o uso do assistente (quem perguntou, quais ferramentas foram chamadas).

## Decisões em aberto

1. **Provedor de LLM e embeddings** (pendente). Opções discutidas:
   - A) Claude (Haiku 5.5) para o assistente + Voyage AI para embeddings — *recomendada*; dois provedores, melhor tool calling.
   - B) OpenAI para tudo — uma chave só, mais simples de operar.
   - C) Embeddings locais (ONNX em .NET) + Claude para chat — sem custo de embeddings, imagem mais pesada.
   - Perguntar se já existe chave/crédito em algum provedor.
2. **Permissões:** o assistente (1) fica só para Admin ou também para Visualizador? (pendente)
3. **Quais funcionalidades implementar primeiro**, dentro das seis acima (o usuário ainda vai decidir).
4. **Funcionalidade 4:** aceita criar o histórico de movimentações como etapa prévia?
5. **Custos e limites:** teto de gastos/limite de requisições por usuário para o assistente.
6. **Privacidade:** o que dos dados de estoque pode ser enviado a provedores externos.

## Ordem sugerida (a confirmar)

1. Infra comum: pgvector + `IEmbeddingService`/`IChatService`.
2. Busca semântica (2) → duplicados (6) → cadastro assistido (3).
3. Assistente com tool calling (1).
4. Movimentações + previsão de reposição (4).
5. Resumo por e-mail (5) — Fase 2.
