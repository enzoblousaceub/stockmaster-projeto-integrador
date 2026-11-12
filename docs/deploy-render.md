# Deploy no Render (passo a passo para iniciante)

Guia para publicar o Stock Master na **sua própria conta** do Render. O Render lê o `render.yaml` na raiz do repositório e cria tudo (banco + site) de uma vez ("Blueprint").

## 1. Pré-requisitos
1. Conta no [Render](https://render.com) (pode entrar com o GitHub).
2. O código no **seu** repositório do GitHub (a branch que você quer publicar). O `git push` é feito por você; nada aqui faz push.
3. Dê ao Render acesso ao repositório (Render → *Account Settings → GitHub*, ou durante o passo 2).
4. Um terminal com PowerShell (Windows) ou bash (Linux/Mac) para gerar as chaves (passo 4).

## 2. Criar o serviço pelo Blueprint
1. No Render: **New + → Blueprint** → escolha o repositório → selecione a branch → **Connect**.
2. O Render mostra o que vai criar: banco `stockmaster-db` (Postgres free) e site `estoque-master` (Docker, free).
3. Ele vai pedir os valores dos 4 segredos (`sync: false`). Preencha com o passo 4 e clique **Apply**.
4. **Nome ocupado?** O nome do site vira parte da URL (`https://<nome>.onrender.com`) e é único no Render. Se `estoque-master` já existir, edite no `render.yaml` a linha `name: estoque-master` para outro nome (ex.: `estoque-master-seunome`), faça commit e push, e crie o Blueprint de novo. O nome do banco (`stockmaster-db`) é só seu e não costuma conflitar.
5. A primeira publicação demora alguns minutos (build do Docker). Acompanhe em **Logs**.

## 3. Banco de dados
- **Variável lida pelo backend: `DATABASE_URL`.** Formato: `postgresql://usuario:senha@host:5432/banco` (também aceita `postgres://`). Sem ela o app não sobe ("Banco nao configurado").
  - Alternativa só para desenvolvimento: `ConnectionStrings__Default` em formato Npgsql (`Host=...;Database=...;Username=...;Password=...`). `DATABASE_URL` tem prioridade.
- **Opção A – Postgres do Render (free):** já vem no `render.yaml`; o Render preenche `DATABASE_URL` sozinho com a URL interna. **Expira 30 dias após a criação** (+14 dias de carência; depois o Render apaga banco e dados), sem backup. Serve para validar, não para dados reais.
- **Opção B – Postgres externo** (Neon, Supabase, Render pago...): no `render.yaml`, apague o bloco `databases:` e troque o `DATABASE_URL` por `value`/`sync: false` (ou, depois de criado, defina `DATABASE_URL` no painel do site → *Environment*) com a URL do provedor. Use a URL com SSL conforme o provedor indicar.
- **Ao subir:** o app aplica as **migrations automaticamente** (cria/atualiza as tabelas). O **seed (dados de demonstração) fica desligado** em produção (`Seed__Enabled=false`); o banco começa vazio. O primeiro Admin é criado a partir dos segredos abaixo.

## 4. Segredos
Preencha no Blueprint (ou depois em *Site → Environment*). Nunca coloque estes valores no repositório.

| Variável | O que é |
|---|---|
| `Auth__AdminEmail` | e-mail (login) do primeiro Admin. Se já existir um Admin de antes do login por e-mail, ele passa a usar este e-mail com a mesma senha |
| `Email__ResendApiKey` / `Email__From` / `App__PublicBaseUrl` | convite por e-mail (Resend). Sem elas o Admin vê o código na tela. Pendente: domínio verificado |
| `Auth__AdminSenha` | senha do primeiro Admin (mín. 8, não pode ser comum). Só é lida na criação; pode remover depois |
| `Auth__CodeHmacKey` | chave base64 de 32 bytes (protege os códigos de ativação). Sem ela, criar usuário dá `503 service_misconfigured` |
| `Auth__KeyEncryptionKey` | chave base64 de 32 bytes (cifra as chaves de sessão no banco). Sem ela o app avisa com erro no log |

Gerar uma chave base64 de 32 bytes (rode **duas vezes**, uma para cada chave, valores diferentes):

- Windows (PowerShell):
  ```powershell
  [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
  ```
- Linux/Mac:
  ```bash
  openssl rand -base64 32
  ```

Guarde as chaves em um gerenciador de senhas: girar `Auth__CodeHmacKey` invalida códigos pendentes; girar `Auth__KeyEncryptionKey` desloga todo mundo.

## 5. Primeiro acesso
1. Abra `https://<nome>.onrender.com` (a primeira abertura pode levar ~1 min: o plano free "acorda").
2. Entre com `Auth__AdminEmail` e `Auth__AdminSenha`.
3. Em **Usuários**, crie a pessoa (usuário + perfil `Admin` ou `Visualizador`). A tela mostra o **código de ativação** (12 caracteres, `XXXX-XXXX-XXXX`, vale **48 h**, uso único) **uma única vez**: copie e passe à pessoa.
4. A pessoa abre a tela de ativação, informa usuário + código + nova senha (mín. 8) e passa a entrar normalmente.
5. Esqueceu a senha de alguém? O Admin usa **Redefinir senha**: gera novo código e invalida a senha atual. O Admin nunca vê senhas.

## 6. Checagens pós-deploy (exigidas pela revisão de segurança)
Troque `SITE` pelo endereço público. Em PowerShell use `curl.exe` (não o alias `curl`).

**a) IP real (X-Forwarded-For).** O limite de falhas por IP e a auditoria dependem do IP do cliente.
- Como: faça login com o Admin de dois lugares/redes diferentes (ex.: PC e celular em 4G). Depois consulte a auditoria (`GET https://SITE/api/audit?limite=20` logado como Admin) e compare o campo `ip` dos eventos de login.
- Esperado: **IPs diferentes**, e nenhum igual ao do proxy do Render (faixas `10.x`/`172.x`). Se vierem iguais, avise: o limite `RateLimit__LoginFailuresPerMinute` (20) deve ficar alto e o proxy precisa de ajuste.

**b) Tempo por hash (ms).** Cada login faz PBKDF2 com 210 mil iterações; no plano free (CPU fraca) pode ser lento.
- Como:
  ```bash
  curl -s -o /dev/null -w "healthz %{time_total}s\n" https://SITE/healthz
  curl -s -o /dev/null -w "login   %{time_total}s\n" -X POST https://SITE/api/auth/login -H "Content-Type: application/json" -d '{"usuario":"admin","senha":"senha-errada-123"}'
  ```
  Repita 3 vezes (a 1ª pode incluir o "acordar"); a diferença login − healthz ≈ custo do hash.
- Esperado: **abaixo de ~500 ms**. Se passar de ~1 s, ou aparecer `503 server_busy` com poucos usuários, reduza `Auth__HashConcurrency` (padrão 3) ou suba de plano. Não reduza as iterações.

**c) X-Forwarded-Proto = https.**
- Como: `curl -i -X POST https://SITE/api/auth/login -H "Content-Type: application/json" -d '{"usuario":"admin","senha":"x"}'`; depois faça login válido no navegador e, nas ferramentas do navegador (*Application → Cookies*), confira os cookies.
- Esperado: o login errado responde **`401` com `code: invalid_credentials`** (nunca `400 https_required`); após login válido existe o cookie **`__Host-sm.auth`** (HttpOnly, Secure, SameSite=Strict). Se vier `400 https_required`, o proxy não está enviando `X-Forwarded-Proto: https`.

**d) Fluxo CSRF: 403 → GET /me → repetir 1 vez.**
- Como (no navegador, logado, com o Console aberto): apague o cookie `XSRF-TOKEN` (*Application → Cookies*) e execute alguma ação que grave (ex.: criar/editar produto). Observe a aba *Network*.
- Esperado: a escrita falha com **`403` `code: csrf_invalid`**; o front chama **`GET /api/auth/me`** (que reemite o cookie `XSRF-TOKEN`) e **repete a requisição uma única vez**, que passa com `2xx`. Se falhar de novo, a tela deve mostrar o erro (sem laço infinito).

**e) CSP servida no domínio público.**
- Como: `curl -sI https://SITE/ | grep -i content-security-policy` (PowerShell: `curl.exe -sI https://SITE/ | Select-String -Pattern content-security-policy`). Teste também `https://SITE/healthz`.
- Esperado: o cabeçalho existe e começa com `default-src 'self'; script-src 'self' 'sha256-...'`, com `frame-ancestors 'none'`. Com o Console do navegador aberto, o app carrega **sem erros de "Refused to ..."**. Também devem existir `X-Content-Type-Options: nosniff` e `Strict-Transport-Security`.

## 7. Limites do plano free e uso real
- O site **dorme após 15 minutos sem acessos**; o primeiro acesso depois disso leva cerca de 1 minuto para acordar.
- O Postgres free **expira em 30 dias** (14 de carência) e **não tem backup**. Depois disso os dados são apagados.
- Para uso real: (1) troque o banco por um plano pago do Render ou um Postgres externo com backup (passo 3, opção B); (2) suba o site para um plano que não durma (*Starter* ou superior); (3) guarde as chaves do passo 4; (4) refaça as checagens do passo 6 após qualquer mudança de plano/domínio.
