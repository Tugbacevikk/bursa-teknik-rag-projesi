#!/bin/bash
# Creates the restricted account the API connects with. POSTGRES_USER owns the tables and
# is used only by migrations. Table privileges are granted in migrations, not here.
#
# Init scripts run only when the volume is created. On an existing database run it once:
#   docker compose exec postgres bash /docker-entrypoint-initdb.d/02-app-user.sh
set -e

: "${APP_DB_PASSWORD:?APP_DB_PASSWORD is not set; add it to .env (see .env.example)}"

psql -v ON_ERROR_STOP=1 \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" \
    -v app_password="$APP_DB_PASSWORD" <<'EOSQL'
CREATE ROLE btu_asistan_app LOGIN PASSWORD :'app_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE;
EOSQL
