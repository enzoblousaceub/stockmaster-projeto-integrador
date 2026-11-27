// Copia os DADOS do banco antigo (Render) para o novo (Supabase), tabela por tabela, via COPY.
// O esquema do destino ja precisa existir (o app cria com as migrations ao subir) e ser da MESMA versao:
// o script compara o __EFMigrationsHistory dos dois lados e aborta se forem diferentes.
//
// Uso (Git Bash), na pasta scripts/copiar-dados, URLs so em variaveis (nunca na linha de comando):
//   npm install
//   read -rs ORIGEM_URL;  export ORIGEM_URL     # Render: External Database URL
//   read -rs DESTINO_URL; export DESTINO_URL    # Supabase: Session pooler (porta 5432)
//   node copiar.mjs              # simulacao: so mostra as contagens dos dois lados
//   node copiar.mjs --confirmar  # APAGA os dados do destino e copia os da origem (transacao unica)
import pg from 'pg';
import { to as copyTo, from as copyFrom } from 'pg-copy-streams';
import { pipeline } from 'node:stream/promises';

const confirmar = process.argv.includes('--confirmar');
const MIGRATIONS = '__EFMigrationsHistory';

function cliente(nome, url) {
  if (!url) { console.error(`Defina ${nome}.`); process.exit(1); }
  const u = new URL(url);
  u.search = ''; // o sslmode da URL e tratado abaixo (no driver pg, "require" exigiria verificar a cadeia)
  const local = ['localhost', '127.0.0.1', '::1'].includes(u.hostname);
  // mesmo comportamento do backend: TLS obrigatorio, sem validar a cadeia (cert interno/autoassinado)
  return new pg.Client({ connectionString: u.toString(), ssl: local ? false : { rejectUnauthorized: false } });
}

const q = (id) => '"' + id.replaceAll('"', '""') + '"';

async function tabelas(c) {
  const { rows } = await c.query(
    `select table_name from information_schema.tables
     where table_schema = 'public' and table_type = 'BASE TABLE' order by 1`);
  return rows.map((r) => r.table_name);
}

async function colunas(c, t) {
  const { rows } = await c.query(
    `select column_name from information_schema.columns
     where table_schema = 'public' and table_name = $1 and is_generated = 'NEVER' order by ordinal_position`, [t]);
  return rows.map((r) => r.column_name);
}

async function migrations(c) {
  const { rows } = await c.query(`select "MigrationId" from public.${q(MIGRATIONS)} order by 1`);
  return rows.map((r) => r.MigrationId).join(',');
}

async function contagens(c, ts) {
  const out = {};
  for (const t of ts) out[t] = Number((await c.query(`select count(*) from public.${q(t)}`)).rows[0].count);
  return out;
}

// ordem por dependencia de chave estrangeira (pais antes dos filhos)
async function ordemFk(c, ts) {
  const { rows } = await c.query(
    `select cl.relname as filho, pr.relname as pai from pg_constraint k
       join pg_class cl on cl.oid = k.conrelid join pg_class pr on pr.oid = k.confrelid
       join pg_namespace n on n.oid = cl.relnamespace
     where k.contype = 'f' and n.nspname = 'public' and cl.relname <> pr.relname`);
  const pais = new Map(ts.map((t) => [t, new Set()]));
  for (const r of rows) pais.get(r.filho)?.add(r.pai);
  const ordem = [];
  while (ordem.length < ts.length) {
    const prox = ts.filter((t) => !ordem.includes(t) && [...pais.get(t)].every((p) => ordem.includes(p)));
    if (prox.length === 0) throw new Error('Ciclo de chaves estrangeiras; ordem indefinida.');
    ordem.push(...prox);
  }
  return ordem;
}

const origem = cliente('ORIGEM_URL', process.env.ORIGEM_URL);
const destino = cliente('DESTINO_URL', process.env.DESTINO_URL);
await origem.connect();
await destino.connect();

try {
  const [mo, md] = [await migrations(origem), await migrations(destino)];
  if (mo !== md) {
    console.error(`Migrations diferentes.\n  origem : ${mo}\n  destino: ${md}\nPublique a mesma versao do app nos dois antes de copiar.`);
    process.exit(1);
  }

  const ts = (await tabelas(origem)).filter((t) => t !== MIGRATIONS);
  const tsDestino = await tabelas(destino);
  for (const t of ts) {
    if (!tsDestino.includes(t)) throw new Error(`Tabela ${t} nao existe no destino.`);
    const [a, b] = [(await colunas(origem, t)).join(','), (await colunas(destino, t)).join(',')];
    if (a !== b) throw new Error(`Colunas de ${t} diferem:\n  origem : ${a}\n  destino: ${b}`);
  }
  const ordem = await ordemFk(origem, ts);

  const antes = await contagens(destino, ts);
  const fonte = await contagens(origem, ts);
  console.log('tabela'.padEnd(22), 'origem'.padStart(8), 'destino'.padStart(8));
  for (const t of ordem) console.log(t.padEnd(22), String(fonte[t]).padStart(8), String(antes[t]).padStart(8));

  if (!confirmar) {
    console.log('\nSimulacao: nada foi alterado. Rode com --confirmar para substituir os dados do destino pelos da origem.');
    process.exit(0);
  }

  console.log('\nCopiando (transacao unica no destino)...');
  await origem.query('begin isolation level repeatable read read only'); // foto consistente da origem
  await destino.query('begin');
  await destino.query(`truncate ${ordem.map((t) => 'public.' + q(t)).join(', ')} restart identity cascade`);

  for (const t of ordem) {
    const cols = (await colunas(origem, t)).map(q).join(', ');
    const leitura = origem.query(copyTo(`copy public.${q(t)} (${cols}) to stdout`));
    const escrita = destino.query(copyFrom(`copy public.${q(t)} (${cols}) from stdin`));
    await pipeline(leitura, escrita);
    console.log(`  ${t}: ok`);
  }

  // sequencias/identity voltam a continuar do maior id copiado
  const { rows: seqs } = await destino.query(
    `select table_name, column_name, pg_get_serial_sequence(format('public.%I', table_name), column_name) as seq
     from information_schema.columns where table_schema = 'public'`);
  for (const s of seqs.filter((r) => r.seq && ordem.includes(r.table_name))) {
    await destino.query(
      `select setval($1, coalesce((select max(${q(s.column_name)}) from public.${q(s.table_name)}), 1),
              (select max(${q(s.column_name)}) from public.${q(s.table_name)}) is not null)`, [s.seq]);
  }

  const depois = await contagens(destino, ts);
  const divergentes = ordem.filter((t) => depois[t] !== fonte[t]);
  if (divergentes.length) {
    await destino.query('rollback');
    throw new Error(`Contagens divergentes em ${divergentes.join(', ')}; nada foi gravado.`);
  }
  await destino.query('commit');
  await origem.query('commit');
  console.log('\nOK: dados copiados e contagens conferidas. Reinicie o servico no Render (Manual Deploy > Restart service).');
} catch (e) {
  console.error('ERRO:', e.message);
  process.exitCode = 1;
} finally {
  await origem.end();
  await destino.end();
}
