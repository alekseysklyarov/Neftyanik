# Production deployment foundation
# Production deployment foundation

This directory contains the Docker-based production deployment foundation for `Neftyanik Portal`.

## Scope

The stack contains:

- `web` - ASP.NET Core 8 application on internal port `8080`;
- `sqlserver` - SQL Server 2022 Linux container with persistent storage;
- `caddy` - reverse proxy with public ports `80` and `443`.

Only `caddy` publishes host ports. `web` and `sqlserver` stay on the private Compose network.

Local Development execution remains unchanged. Visual Studio and `dotnet run` continue using the existing Development configuration and the local Development database.

## Prerequisites

Prepare an Ubuntu host with:

- Docker Engine;
- Docker Compose plugin (`docker compose`);
- a public DNS record pointing the production domain to the host;
- inbound TCP ports `80` and `443` open to the host;
- enough disk space for SQL Server and Caddy named volumes.

## Required environment variables

Create a real deployment environment file from the template:

```sh
cp deploy/.env.example deploy/.env
nano deploy/.env
```

Required variables:

- `DOMAIN` - public DNS name used by Caddy automatic HTTPS;
- `MSSQL_SA_PASSWORD` - strong SQL Server SA password;
- `DB_NAME` - application database name.

Optional variables are included only for explicit bootstrap commands such as `create-admin`.

## Validate the Compose configuration

Validate the resolved configuration before deployment:

```sh
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml config
```

If the trusted reverse-proxy IP or subnet is changed later, update the matching `ReverseProxy__Known*` environment values in `deploy/docker-compose.production.yml` before deployment.

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
