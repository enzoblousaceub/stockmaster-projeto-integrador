# 📦 Stock Master — Sistema de Gestão de Estoque

Bem-vindo ao **Stock Master** (StockManager), uma plataforma moderna para monitoramento, organização e controle de estoque desenvolvida em **.NET** e **Angular**.

Este documento fornece o guia completo de configuração, execução e arquitetura do projeto.

---

## 🚀 Como Começar

### Pré-requisitos

Certifique-se de ter instalado:
*   **[.NET SDK 10](https://dotnet.microsoft.com/download)** (Backend)
*   **[Docker Desktop](https://www.docker.com/products/docker-desktop/)** (PostgreSQL local e testes)
*   **[Node.js 18+](https://nodejs.org/)** (Frontend)
*   **Angular CLI** (Opcional, pode ser executado via `npx`)

---

## 🛠️ Instalação e Execução

### 1. Clonar o Repositório
```bash
git clone <url-do-repositorio>
cd newrepositorygithub
```

### 2. Configurar e Executar o Backend
O backend utiliza **ASP.NET Core (.NET 10)** com **PostgreSQL** e Entity Framework Core (migrations aplicadas automaticamente na inicialização).
```bash
# na raiz do projeto: banco local (Docker Desktop precisa estar aberto)
cp .env.example .env          # edite a senha; o .env nunca vai para o git
docker compose up -d          # postgres:17, exposto só em localhost:5432

cd backend
export DATABASE_URL="postgresql://stockmaster:<senha do .env>@localhost:5432/stockmaster"   # PowerShell: $env:DATABASE_URL="..."
dotnet run
```
> [!IMPORTANT]
> O backend roda em **http://localhost:8080**. Em desenvolvimento (`dotnet run`) os dados de demonstração são criados na primeira execução (`Seed:Enabled=true` em `appsettings.Development.json`). Em produção o seed fica **desligado** (`Seed__Enabled=false`).
> Testes: `dotnet test backend.Tests` (sobe um PostgreSQL real via Testcontainers; exige o Docker em execução).

#### Autenticação (login por usuário)
Toda a API exige login (exceto `POST /api/auth/login`, `POST /api/auth/ativar` e `/healthz`). O primeiro **Admin** é criado na inicialização a partir de `Auth__AdminEmail` e `Auth__AdminSenha` (o **login é por e-mail e senha**) (no `.env.example` há valores **apenas de exemplo para desenvolvimento**; nunca há senha padrão no código). Sem essas variáveis o app sobe **sem Admin** e registra um erro no log. O Admin cadastra as pessoas (`/api/users`), a pessoa recebe um **e-mail de convite** com um link (`/ativar#email=…&codigo=…`) e um **código de uso único (48 h)** e define a própria senha em `/api/auth/ativar`. O e-mail é enviado pelo Resend (`Email__ResendApiKey`, `Email__From`, `App__PublicBaseUrl`); sem isso, ou se o envio falhar, o Admin vê o código na tela e o repassa. **Pendente para produção:** domínio próprio verificado no Resend. **Migração:** contas antigas (que usavam nome de usuário) precisam de e-mail: o Admin existente recebe `Auth__AdminEmail` no startup (mesma senha); outras contas sem e-mail não conseguem entrar e são listadas no log (desative e recrie). O contrato completo está na nota `contrato-auth` do projeto.

Variáveis (produção): `Auth__AdminEmail`, `Auth__AdminSenha`, `Auth__CodeHmacKey` e `Auth__KeyEncryptionKey` (as duas últimas: base64 de pelo menos 32 bytes, ex.: `openssl rand -base64 32`; **troque `Auth__AdminSenha` do exemplo**). Girar `Auth__CodeHmacKey` invalida códigos de ativação pendentes; girar `Auth__KeyEncryptionKey` torna as chaves de sessão ilegíveis e **desloga todo mundo** (o app gera chave nova e segue). Se o banco já teve chaves gravadas em texto (deploy/dev anterior sem a KEK), limpe uma vez a tabela `DataProtectionKeys` ao ativar a KEK (todos precisam logar de novo); senão as chaves antigas valem até expirar (90 dias). Em Production sem a KEK o app registra um erro no log na inicialização. Em `http://localhost` sem HTTPS use `Auth__SecureCookies=false` (em `Development` já vem assim); em produção o proxy precisa enviar `X-Forwarded-Proto: https`, senão a API responde `400 https_required` e registra um erro no log.

### 3. Configurar e Executar o Frontend
O frontend utiliza **Angular 21** com **Angular Material** e modo **Zoneless**.
```bash
cd ../frontend
npm install
npm start
```
> [!NOTE]
> A aplicação web estará disponível em **http://localhost:4200**.
> As requisições para `/api` são automaticamente redirecionadas para `http://localhost:8080` através do `proxy.conf.json`.

---

## 📖 Funcionalidades da Aplicação

### Perfis de acesso
| Perfil | O que pode fazer |
|---|---|
| **Admin** | Tudo: cadastrar/editar/excluir produtos, imagens, movimentar estoque, gerenciar usuários |
| **Operador** | Registrar entradas, saídas e conferências; ver produtos, fichas e relatórios |
| **Visualizador** | Somente leitura (produtos, fichas, relatórios) |

### 1. Dashboard
*   **Indicadores:** total de produtos, itens em estoque, valor do estoque (a preço de venda e a custo) e alertas.
*   **Precisa de reposição:** itens abaixo do mínimo; o botão **Repor** registra a entrada da mercadoria que chegou.
*   **Estoque por categoria.**

### 2. Produtos (lista)
*   Busca, filtro por categoria, ordenação e paginação; status 🟢 Em estoque, 🟡 Baixo, 🔴 Esgotado.
*   Código do produto (`#0001`, RF002) e miniatura da imagem.
*   **Movimentar** (ícone ⇅): abre o registro de entrada/saída/conferência. Os botões **+/−** (celular) fazem ajuste rápido.

### 3. Movimentações de estoque (RF006, RF007, RF008)
*   **Entrada:** quantidade, data e motivo (compra, devolução de cliente...).
*   **Saída:** quantidade, data e **motivo obrigatório** (venda, perda/avaria, uso interno...). O sistema não deixa o saldo ficar negativo.
*   **Conferência:** informa o saldo contado e o sistema registra a diferença como ajuste (inventário).
*   O saldo é atualizado automaticamente e **sempre bate com a soma do histórico**: cadastro com quantidade inicial gera "Estoque inicial", e ajustes rápidos/edições geram "Ajuste".
*   Registros simultâneos são seguros (a linha do produto é travada na transação).

### 4. Ficha do produto
*   Dados, imagem, custo, preço, margem e valor em estoque.
*   **Previsão:** consumo médio diário, em quantos dias o produto acaba e sugestão de compra.
*   Histórico completo de movimentações (data, tipo, quantidade, saldo após, motivo, usuário).

### 5. Relatórios (RF010, RF011)
*   Período de 7, 30 ou 90 dias: entradas, saídas, vendas e margem estimadas, estoque a custo e a preço.
*   Gráfico de entradas e saídas por dia.
*   **Reposição sugerida:** produtos abaixo do mínimo ou que acabam em até 14 dias; sugestão = consumo de 30 dias + estoque mínimo − saldo, com custo estimado da compra.
*   **Mais vendidos**, **parados** (capital imobilizado) e previsão dos demais produtos.
*   A previsão é estatística simples (média de saídas no período); ajustes de conferência não contam como consumo.

### 6. Cadastro e edição (Admin)
*   Nome, descrição, categoria (com sugestões), **preço de venda**, **custo**, quantidade inicial e estoque mínimo.
*   **Imagem** (RF003): JPEG, PNG ou WebP até 2 MB, guardada no PostgreSQL (o disco do Render é temporário). O tipo é conferido pelo conteúdo do arquivo.
*   Na edição, o saldo fica bloqueado: muda-se por movimentação, para manter o histórico.

### 7. Usuários e auditoria (Admin)
*   Convite por e-mail, perfis, ativação/desativação. A auditoria registra login, usuários e também cadastro, edição, exclusão e imagem de produtos.

### API (resumo)
| Método e rota | Perfil |
|---|---|
| `GET /api/products`, `/stats`, `/categories`, `/{id}`, `/{id}/image` | todos |
| `POST/PUT/DELETE /api/products`, `PUT/DELETE /api/products/{id}/image` | Admin |
| `POST /api/products/{id}/movements` `{type, quantity, reason, occurredAt}` | Admin, Operador |
| `PATCH /api/products/{id}/quantity` `{delta}` (ajuste rápido) | Admin, Operador |
| `GET /api/products/{id}/movements`, `GET /api/movements?type=&from=&to=&limit=` | todos |
| `GET /api/reports/summary?days=30` | todos |

---

## 🏗️ Estrutura do Projeto

```txt
newrepositorygithub/
├── backend/                  # ASP.NET Core Web API (.NET 10)
│   ├── Controllers/          # Products, Movements, Reports, Auth, Users
│   ├── Services/             # StockService (saldo + histórico), ForecastService (relatórios/previsão), ImageSniffer
│   ├── Data/                 # AppDbContext, DbInitializer (seed com 45 dias de histórico)
│   ├── Migrations/           # EF Core (aplicadas na inicialização)
│   ├── Models/ e DTOs/       # Product, StockMovement, ProductImage, AppUser...
│   └── Security/             # autenticação por cookie, CSRF, rate limit, auditoria, e-mail
├── backend.Tests/            # testes de integração (PostgreSQL real)
├── frontend/                 # Angular 21 (PWA)
│   ├── src/app/pages/        # dashboard, products, product-detail, product-form, reports, users, auth
│   ├── src/app/components/   # diálogos (movimentação, exclusão, usuários, configurações)
│   ├── src/app/core/         # autenticação, guards, interceptor, erros da API
│   └── src/app/services/     # integração HTTP
├── docs/                     # deploy no Render, Resend, planejamento de IA
└── .github/workflows/ci.yml  # build e testes (o deploy é feito pelo Render)
```

### Testes
```bash
dotnet test backend.Tests            # sobe um PostgreSQL via Testcontainers (Docker)
# sem Docker: aponte para um PostgreSQL já rodando
TEST_POSTGRES="Host=127.0.0.1;Port=5432;Username=postgres;Password=..." dotnet test backend.Tests
cd frontend && npm test -- --watch=false
```

---

## 🧪 Resolução de Problemas

*   **Conflito de Porta:** Se a porta 8080 ou 4200 estiver em uso, encerre o processo anterior antes de reiniciar o serviço.
*   **CORS / Comunicação:** O frontend está configurado para consumir `/api` via proxy reverso no desenvolvimento (`npm start`) e relativo em produção quando hospedado junto ao backend.
*   **Banco de Dados:** PostgreSQL via `DATABASE_URL` (formato `postgresql://usuario:senha@host:5432/banco`, o mesmo que o Render fornece) ou `ConnectionStrings:Default`. Para resetar os dados locais: `docker compose down -v` e suba de novo.

## ☁️ Deploy no Render

O `render.yaml` cria o serviço web (Docker) e um Postgres ligado a ele via `DATABASE_URL`.

> [!WARNING]
> O Postgres do `render.yaml` está no plano **gratuito**, apenas para validação: **expira 30 dias após a criação** (14 dias de carência, depois o Render apaga o banco e os dados) e **não tem backup**. **Antes de usar dados reais, troque `plan: free` por um plano pago.**

> [!NOTE]
> **Atualização com movimentações:** a migration `StockMovementsCostImages` cria o histórico e registra o saldo atual de cada produto existente como um ajuste "Saldo anterior ao historico" (não conta como entrada nem como consumo). O custo dos produtos existentes começa em R$ 0,00: preencha na edição para os relatórios de margem e valor a custo ficarem corretos.

### Primeiro acesso e checagens pós-deploy
1. No painel do Render, preencha os segredos `Auth__AdminEmail`, `Auth__AdminSenha`, `Auth__CodeHmacKey` e `Auth__KeyEncryptionKey` (e, para o convite por e-mail, `Email__ResendApiKey`, `Email__From` e `App__PublicBaseUrl`) (declarados no `render.yaml` com `sync: false`). Faça o primeiro login com o Admin e crie as demais pessoas.
2. **IP de origem (X-Forwarded-For):** o limite de falhas por IP (`RateLimit__LoginFailuresPerMinute`, 20 por ora) e a auditoria dependem de o IP real chegar à aplicação. Logue `RemoteIpAddress` de dois clientes diferentes; se vierem iguais (IP da borda), mantenha o limite alto ou ajuste o proxy.
3. **Custo do hash:** meça ms por hash no plano free (PBKDF2 210k iterações); a concorrência é limitada por `Auth__HashConcurrency` (padrão 3) e, saturado, a API responde `503 server_busy`.
4. **HTTPS:** confirme que o login funciona (o proxy envia `X-Forwarded-Proto: https`).

---
*Stock Master — Gestão inteligente e simplificada de inventário.*
