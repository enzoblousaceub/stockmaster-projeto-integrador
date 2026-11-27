#!/usr/bin/env bash
# Copia o banco do Render (origem) para o Supabase (destino), esquema + dados.
# Usa pg_dump/psql da imagem postgres:17 (so precisa do Docker).
#
# Uso (Git Bash), com as URLs em variaveis para nao ficarem no historico do shell:
#   read -rs ORIGEM_URL;  export ORIGEM_URL    # External Database URL do Render
#   read -rs DESTINO_URL; export DESTINO_URL   # Session pooler do Supabase (porta 5432)
#   bash scripts/migrar-banco.sh
#
# O destino precisa estar VAZIO (projeto Supabase novo). O dump inclui a tabela
# __EFMigrationsHistory, entao o app reconhece as migrations ja aplicadas.
set -euo pipefail

: "${ORIGEM_URL:?defina ORIGEM_URL (External Database URL do Render)}"
: "${DESTINO_URL:?defina DESTINO_URL (Supabase Session pooler)}"

add_ssl() { case "$1" in *sslmode=*) echo "$1";; *\?*) echo "$1&sslmode=require";; *) echo "$1?sslmode=require";; esac; }
ORIGEM_URL=$(add_ssl "$ORIGEM_URL")
DESTINO_URL=$(add_ssl "$DESTINO_URL")

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
PG="docker run --rm -i -e PGCONNECT_TIMEOUT=30 postgres:17"

echo ">> Conferindo o destino (precisa estar vazio)..."
existentes=$($PG psql "$DESTINO_URL" -Atc "select count(*) from pg_tables where schemaname='public'")
if [ "$existentes" != "0" ]; then
  echo "ERRO: o schema public do destino ja tem $existentes tabela(s). Abortado para nao misturar dados." >&2
  exit 1
fi

echo ">> Exportando do Render (schema public, sem donos/permissoes)..."
$PG pg_dump "$ORIGEM_URL" --schema=public --no-owner --no-privileges --no-comments \
  > "$WORK/dump.sql"
# 'CREATE SCHEMA public' ja existe no Supabase; o pg_dump 17 tambem emite SET transaction_timeout (so PG >= 17)
sed -i -e '/^CREATE SCHEMA public;/d' -e '/^SET transaction_timeout/d' "$WORK/dump.sql"
echo "   $(wc -c < "$WORK/dump.sql") bytes exportados."

echo ">> Importando no Supabase (transacao unica: falhou, nada fica gravado)..."
$PG psql "$DESTINO_URL" -v ON_ERROR_STOP=1 --single-transaction -q < "$WORK/dump.sql"

echo ">> Protegendo as tabelas contra a Data API do Supabase (RLS ligado, sem policies)..."
$PG psql "$DESTINO_URL" -v ON_ERROR_STOP=1 -q <<'SQL'
do $$
declare t record;
begin
  for t in select tablename from pg_tables where schemaname = 'public' loop
    execute format('alter table public.%I enable row level security', t.tablename);
    execute format('revoke all on table public.%I from anon, authenticated', t.tablename);
  end loop;
end $$;
SQL

echo ">> Comparando contagens de linhas (origem x destino)..."
CONTA="select string_agg(format('%s=%s', table_name, (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from public.%I', table_name), false, true, '')))[1]::text), ' ' order by table_name) from information_schema.tables where table_schema='public' and table_type='BASE TABLE'"
o=$($PG psql "$ORIGEM_URL" -Atc "$CONTA")
d=$($PG psql "$DESTINO_URL" -Atc "$CONTA")
echo "   origem : $o"
echo "   destino: $d"
if [ "$o" = "$d" ]; then echo ">> OK: contagens iguais."; else echo "ERRO: contagens diferentes." >&2; exit 1; fi
