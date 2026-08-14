# Production deployment foundation
# Production deployment foundation

This directory contains the Docker-based production deployment foundation for `Neftyanik Portal`.

## Scope

The stack contains:

- `web` - ASP.NET Core 8 application on internal port `8080`;
- `sqlserver` - SQL Server 2022 Linux container with persistent storage;
- `caddy` - reverse proxy with public ports `80` and `443`.

`caddy` publishes public HTTP and HTTPS ports. `web` stays on the private Compose network, and `sqlserver` is exposed only on `127.0.0.1:1433` for the existing SSH tunnel workflow.

Local Development execution remains unchanged. Visual Studio and `dotnet run` continue using the existing Development configuration and the local Development database.

## Prerequisites

Prepare a Debian or Ubuntu host with:

- Docker Engine;
- Docker Compose plugin (`docker compose`);
- inbound TCP port `80` open to the host;
- inbound TCP port `443` open to the host;
- inbound UDP port `443` open to the host for HTTP/3;
- enough disk space for SQL Server and Caddy named volumes.

Ports `80` and `443` must be reachable from the Internet so Caddy can obtain and renew HTTPS certificates automatically.

## Required environment variables

Create a real deployment environment file from the template:

```sh
cp deploy/.env.example deploy/.env
nano deploy/.env
```

Required variables:

- `SITE_ADDRESS` - Caddy site address. For production, set `SITE_ADDRESS=dachahub.com.ua` in the untracked `deploy/.env` file;
- `REQUIRE_HTTPS` - ASP.NET Core HTTPS enforcement toggle. Keep this enabled for the domain-based production deployment;
- `MSSQL_SA_PASSWORD` - strong SQL Server SA password;
- `DB_NAME` - application database name.

Optional variables are included only for explicit bootstrap commands such as `create-admin`.

Example values:

Tracked template in `deploy/.env.example`:

```env
SITE_ADDRESS=example.com
REQUIRE_HTTPS=true
```

Real production values in the untracked `deploy/.env`:

```env
SITE_ADDRESS=dachahub.com.ua
REQUIRE_HTTPS=true
```

Do not commit the real production domain value in `deploy/.env`.

## SSH access and SQL Server tunnel

Connect to the Debian host over its configured SSH port:

```sh
ssh -p SSH_PORT root@SERVER_IP
```

To access SQL Server securely from a workstation, create an SSH tunnel:

```sh
ssh -p SSH_PORT -L 14330:127.0.0.1:1433 root@SERVER_IP
```

Then connect from SSMS to:

```text
localhost,14330
```

`sqlserver` remains bound only to `127.0.0.1:1433:1433` on the host and is not publicly exposed.

## Validate the Compose configuration

Validate the resolved configuration before deployment:

```sh
docker compose --env-file deploy/.env.example -f deploy/docker-compose.production.yml config
```

If the trusted reverse-proxy IP or subnet is changed later, update the matching `ReverseProxy__Known*` environment values in `deploy/docker-compose.production.yml` before deployment.

The Compose file keeps SQL Server off the public network. Host access for the existing SSH tunnel is limited to `127.0.0.1:1433:1433`.

The public Caddy port mappings are:

```text
80:80
443:443
443:443/udp
```

## Build the containers

From the repository root:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml build
```

To build only the web image:

```sh
docker build -f src/Neftyanik.Portal.Web/Dockerfile -t neftyanik-portal-web:production .
```

## Safe first-deployment migration strategy

Production database migrations are **not** applied automatically by the normal `web` container.

Use the explicit one-shot `migrate` service instead. It uses the same production configuration pattern as the `web` service, applies the existing EF Core migrations with `Database.Migrate()`, and is safe to repeat.

First deployment sequence:

1. start SQL Server;
2. wait until it is healthy;
3. run migrations explicitly;
4. start `web` and `caddy`.

Commands:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml up -d sqlserver

docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml --profile migration run --rm migrate

docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml up -d web caddy
```

For later releases, build the updated image, rerun the migration command, and then restart the long-running services.

Current hardening note: the template uses the SQL Server `sa` login internally for first deployment simplicity. Replacing it with a least-privilege application login remains a required hardening step before final public launch.

## Start the full stack

After migrations are applied:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml up -d
```

## Check status and logs safely

Show container status:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml ps
```

Show logs for the full stack:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml logs -f
```

Show logs for one service:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml logs -f web
```

Check the internal health endpoint from the `web` container:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml exec web curl --fail http://127.0.0.1:8080/health
```

## Update the application

Typical update flow:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml build web

docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml --profile migration run --rm migrate

docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml up -d web caddy
```

## Stop without deleting data

Stop containers but keep named volumes:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml stop
```

Remove containers and networks while preserving named volumes:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml down
```

`docker compose down` preserves named volumes. It does **not** delete the SQL Server database, Caddy state, or Data Protection keys.

## Dangerous commands that delete persistent data

Do **not** run these casually:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml down -v
```

That deletes these named volumes:

- `neftyanik_sqlserver_data`;
- `neftyanik_caddy_data`;
- `neftyanik_caddy_config`;
- `neftyanik_aspnet_data_protection_keys`.

These commands are also destructive because they remove persistent volumes directly:

```sh
docker volume rm neftyanik_sqlserver_data

docker volume rm neftyanik_caddy_data neftyanik_caddy_config neftyanik_aspnet_data_protection_keys
```

`docker system prune --volumes` can also remove unused volumes and should be treated as destructive.

## Optional explicit administrator bootstrap

Only if an initial administrator account is needed and the application database is already migrated:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml run --rm \
  -e NEFTYANIK_ADMIN_EMAIL=admin@example.com \
  -e NEFTYANIK_ADMIN_PASSWORD='ChangeThis_AdminPassword123!' \
  -e NEFTYANIK_ADMIN_NAME='Portal Administrator' \
  web create-admin
```

Do not store real administrator passwords in tracked files.
