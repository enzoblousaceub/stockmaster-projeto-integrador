# Pendências: e-mail de convite (Resend)

O convite de cadastro por e-mail já está no código (PR #3). Para ele **funcionar em produção** faltam as etapas abaixo.
Enquanto elas não forem feitas, o sistema continua funcionando pelo **fallback**: o Admin vê o código de ativação na tela e o repassa à mão.

Serviço no Render: `estoque-master` (https://estoque-master-w4xl.onrender.com).

## Situação atual

| Item | Estado |
|---|---|
| Login por e-mail e senha | Feito |
| Convite por e-mail no código (link + código de uso único, 48 h) | Feito |
| `Auth__AdminEmail` e `App__PublicBaseUrl` no Render | Feito |
| Conta no Resend e API key | **Pendente** |
| `Email__ResendApiKey` e `Email__From` no Render | **Pendente** |
| Domínio próprio verificado no Resend | **Pendente (bloqueia produção real)** |
| Remover `Auth__AdminUsuario` do Render | **Pendente** (só depois de confirmar que o deploy novo está no ar) |

## Variáveis de ambiente envolvidas

| Variável | Valor | Onde |
|---|---|---|
| `Email__ResendApiKey` | Chave do Resend (segredo) | Render → Environment |
| `Email__From` | Remetente, ex.: `Stock Master <acesso@mail.seudominio.com.br>` | Render → Environment |
| `App__PublicBaseUrl` | `https://estoque-master-w4xl.onrender.com` (já configurada) | Render → Environment |

> Nunca coloque a API key no repositório, em chat ou em issue. Ela vai só no painel do Render.

---

## Etapa 0: conferir o deploy

1. No Render, em **Deploys**, confirme que o commit do PR #3 foi publicado (o gatilho do deploy é **Manual**: merge sozinho não publica; use *Manual Deploy → Deploy latest commit*).
2. Em **Logs**, procure `Conta '…' migrada para o e-mail …`.
3. Entre no sistema com o e-mail do `Auth__AdminEmail` e a senha de sempre.
4. Se outras contas antigas (sem e-mail) aparecerem no log como "NAO consegue entrar", desative-as e recrie com o e-mail da pessoa.
5. Só então remova `Auth__AdminUsuario` do Render (ela não é mais usada).

## Etapa 1: conta e chave no Resend

1. Crie uma conta em https://resend.com.
2. Vá em **API Keys → Create API Key**, com permissão **Sending access** (somente enviar).
3. Copie a chave na hora (ela só aparece uma vez).

## Etapa 2: validar o fluxo sem domínio (teste)

O remetente de teste do Resend só entrega para o **e-mail dono da conta Resend**.

1. No Render (**Environment → Edit**), adicione:
   - `Email__ResendApiKey` = a chave criada
   - `Email__From` = `Stock Master <onboarding@resend.dev>`
2. Salve e aguarde o serviço reiniciar.
3. No sistema, em **Usuários → Novo usuário**, crie alguém com o **mesmo e-mail da conta Resend**.
   - Esse e-mail deve ser **diferente do e-mail do Admin** (o e-mail é único). Se for o mesmo, cadastre a conta Resend com outro e-mail seu.
4. Verifique:
   - O e-mail "Segue convite para criar conta" chegou (confira também o spam).
   - O link abre `/ativar` com e-mail e código preenchidos.
   - Dá para criar a senha e entrar.
   - O Admin viu apenas o aviso "Convite enviado" (sem o código na tela).

### Se o e-mail não chegar
- Render → **Logs**: procure `Resend recusou o envio (HTTP …)` (o número indica a causa: 401/403 = chave ou remetente inválido; 422 = destinatário/remetente não permitido; 429 = limite).
- `E-mail nao configurado` no log = `Email__ResendApiKey` ou `Email__From` ausentes ou vazias.
- O Admin vê o código na tela como fallback, então o usuário criado não fica bloqueado.
- A tela de auditoria registra `convite_email` com `ok` ou `falha`.

## Etapa 3: produção real (precisa de domínio)

Sem domínio verificado o Resend **não envia para qualquer pessoa**. Esta é a pendência que bloqueia o uso real.

1. Tenha um domínio (ex.: comprado no Registro.br). Recomendado usar um subdomínio só para e-mail, como `mail.seudominio.com.br`.
2. No Resend, em **Domains → Add Domain**, informe o domínio/subdomínio.
3. O Resend lista registros DNS (SPF, DKIM e um MX de retorno). No painel DNS do registrador, crie **exatamente** esses registros.
4. Volte ao Resend e clique em **Verify** (a propagação pode levar de minutos a horas).
5. No Render, troque `Email__From` para um endereço do domínio verificado, ex.: `Stock Master <acesso@mail.seudominio.com.br>`.
6. Refaça o teste da Etapa 2 com um e-mail qualquer (de preferência de outra pessoa).

## Cuidados

- E-mail de domínio novo pode cair em spam no começo; SPF e DKIM corretos reduzem isso.
- Confira os limites atuais do plano gratuito do Resend na página de preços.
- Mantenha `Email__ResendApiKey` apenas no Render. Se vazar, revogue a chave no Resend e crie outra.
- No plano Free o Render "dorme" por inatividade: o primeiro acesso ao link do convite pode demorar cerca de 50 segundos.
- Fora do escopo desta entrega (próxima etapa, também depende do domínio): "Esqueci minha senha" feito pela própria pessoa.

## Checklist rápido

- [ ] Deploy do PR #3 publicado e login do Admin por e-mail funcionando
- [ ] `Auth__AdminUsuario` removida do Render
- [ ] Conta no Resend criada e API key (Sending access) gerada
- [ ] `Email__ResendApiKey` e `Email__From` (teste) no Render
- [ ] Convite de teste recebido e cadastro concluído pelo link
- [ ] Domínio registrado e verificado no Resend (SPF/DKIM)
- [ ] `Email__From` trocado para o domínio verificado
- [ ] Convite enviado a um e-mail externo com sucesso
