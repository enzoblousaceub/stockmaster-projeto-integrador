# Migração do banco: Render Postgres → Supabase (plano Free)

O Postgres free do Render expira 30 dias após a criação (o nosso foi criado por volta de 07/11/2026) e não tem backup.
O banco passa a ficar no **Supabase Free**; o site continua no **Render Free**. Tudo sem custo.

## Por que Supabase e o que muda

| | Render Postgres Free | Supabase Free |
|---|---|---|
| Validade | expira em 30 dias | não expira |
| Espaço | 1 GB | 500 MB por projeto |
| Inatividade | — | **projeto pausa após 7 dias sem uso** (reativa no painel, dados mantidos) |
| Backup | não | não (no Free; faça `pg_dump` de tempos em tempos) |

O código não muda: o backend já lê `DATABASE_URL` no formato `postgresql://…` e exige TLS fora de localhost.

## 1. Criar o projeto no Supabase
1. https://supabase.com/dashboard → **New project** (organização no plano Free).
2. Nome `stockmaster`, região **West US (North California)** (`us-west-1`, a mais próxima do Render Oregon: menos latência por consulta).
3. Gere uma **senha forte** do banco e guarde no gerenciador de senhas.
4. **Desligue a Data API** (Project Settings → Data API → desativar). O app fala direto com o Postgres;
   a API REST do Supabase só abriria as tabelas para a internet. O script de migração também liga RLS em todas as tabelas, por garantia.

## 2. Pegar a URL de conexão certa
Botão **Connect** no topo do projeto → **Session pooler** (porta **5432**):

```
postgresql://postgres.<ref-do-projeto>:<SENHA>@aws-0-<regiao>.pooler.supabase.com:5432/postgres
```

- **Não use** a "Direct connection" (`db.<ref>.supabase.co`): é só IPv6 e o Render não conecta por IPv6.
- **Não use** o "Transaction pooler" (porta 6543): quebra o lock das migrations do EF e os prepared statements.
- Se a senha tiver caracteres como `@ : / ? #`, codifique-os na URL (ex.: `@` → `%40`).

## 3. Copiar os dados do Render
Precisa só do Docker Desktop aberto. No Render: banco `stockmaster-db` → **Connect → External Database URL**.

```bash
read -rs ORIGEM_URL;  export ORIGEM_URL     # cole a External URL do Render
read -rs DESTINO_URL; export DESTINO_URL    # cole a URL do Session pooler
bash scripts/migrar-banco.sh
```

O script recusa um destino que já tenha tabelas, importa tudo numa transação única, liga RLS e compara as contagens de linhas.
**Faça a cópia com o site sem uso** (ou pausado), para nada ser gravado no Render durante a cópia.

> Banco do Render já apagado ou sem dados importantes? Pule este passo: ao subir, o app cria as tabelas sozinho
> (migrations) e o primeiro Admin a partir de `Auth__AdminEmail`/`Auth__AdminSenha`. Depois, ligue o RLS
> rodando o bloco "Protegendo as tabelas" do script no SQL Editor do Supabase.

## 4. Apontar o Render para o Supabase
1. Render → serviço `estoque-master` → **Environment** → `DATABASE_URL` = URL do Session pooler → **Save**.
   (Se ela estava ligada ao banco do Render, remova o vínculo e crie como valor comum.)
2. Aguarde o restart e confira em **Logs** que não há erro de conexão; abra `https://<site>/healthz` → `{"status":"ok"}`.
3. Entre no sistema e confira produtos e usuários.
4. Só depois de alguns dias funcionando: apague o banco `stockmaster-db` no Render.

As chaves de sessão ficam no banco (Data Protection), então elas vêm junto na cópia e ninguém precisa logar de novo.

## 5. Deploy automático (CI/CD)
O workflow `.github/workflows/ci.yml` roda build e testes em todo push/PR. Na **main**, se tudo passar,
ele chama o **Deploy Hook** do Render e espera o `/healthz` responder.

Configuração (uma vez):
1. Render → `estoque-master` → **Settings → Build & Deploy**:
   - **Auto-Deploy: Off** (quem publica é o CI, só com testes verdes; evita deploy em dobro).
   - **Deploy Hook**: copie a URL (é um segredo: quem tiver ela dispara deploys).
2. GitHub → repositório → **Settings → Environments → New environment** `production`.
   - **Environment secret** `RENDER_DEPLOY_HOOK_URL` = URL do Deploy Hook.
   - **Environment variable** `APP_URL` = `https://estoque-master-w4xl.onrender.com` (sem barra final).
3. Teste: aba **Actions → CI → Run workflow** na `main`.

Observação: o passo do `/healthz` confirma que o site responde; o resultado do deploy em si
(build do Docker) aparece em Render → **Events/Deploys**. Se o build falhar, o Render mantém a versão anterior no ar.

## 6. Manutenção no plano Free
- **Pausa por inatividade (7 dias):** se o sistema ficar uma semana sem uso, o Supabase pausa o projeto e o site responde `503`
  no `/healthz`. Reative em Supabase → projeto → **Restore**. Para apresentações, abra o sistema na véspera.
- **Backup manual** (recomendado antes de mudanças grandes):
  ```bash
  read -rs DESTINO_URL
  docker run --rm -i postgres:17 pg_dump "$DESTINO_URL?sslmode=require" --schema=public --no-owner > backup-$(date +%F).sql
  ```
  (não commite o arquivo de backup: ele tem os dados e hashes de senha.)
