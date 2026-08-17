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

- `git`;
- `curl`;
- `flock` from `util-linux`;
- Docker Engine;
- Docker Compose plugin (`docker compose`);
- inbound TCP port `80` open to the host;
- inbound TCP port `443` open to the host;
- inbound UDP port `443` open to the host for HTTP/3;
- enough disk space for SQL Server, Caddy named volumes, and SQL backup files.

Ports `80` and `443` must be reachable from the Internet so Caddy can obtain and renew HTTPS certificates automatically.

## GitHub production environment

The production deployment workflow is stored in `.github/workflows/deploy-production.yml` and is started manually from:

`GitHub repository -> Actions -> Deploy production -> Run workflow`

Use the GitHub environment named `production` and configure these required secrets there:

- `PROD_SSH_PRIVATE_KEY`;
- `PROD_SSH_HOST`;
- `PROD_SSH_PORT`;
- `PROD_SSH_USER`;
- `PROD_SSH_KNOWN_HOSTS`.

Notes:

- store the deployment SSH private key only in GitHub Actions secrets;
- keep `StrictHostKeyChecking` enabled by providing the correct `known_hosts` entry;
- do not store `deploy/.env`, SQL passwords, or other production secrets in GitHub;
- the workflow deploys only the tested `master` branch commit that triggered the run.

## VPS preparation

Expected production repository layout:

- repository path: `/opt/Neftyanik`;
- production branch: `master`;
- Git remote used by the deployment script: `origin`;
- production environment file: `/opt/Neftyanik/deploy/.env`;
- durable SQL backup directory: `/opt/Neftyanik/deploy/backups/sqlserver`.

Prepare the VPS once:

1. clone the repository to `/opt/Neftyanik` if it is not already there;
2. ensure the checked-out branch is `master`;
3. ensure the repository remote is named `origin` and points to the production GitHub repository;
4. create `/opt/Neftyanik/deploy/.env` from the template and fill in real production values;
5. create `/opt/Neftyanik/deploy/backups/sqlserver` and make it writable for the SQL Server container;
6. add the GitHub Actions deployment public key to `~/.ssh/authorized_keys` for the chosen SSH user;
7. ensure the SSH user can run `docker compose` and manage the running containers.

Example backup directory preparation:

```sh
mkdir -p /opt/Neftyanik/deploy/backups/sqlserver
chown -R 10001:0 /opt/Neftyanik/deploy/backups/sqlserver
chmod -R 0770 /opt/Neftyanik/deploy/backups/sqlserver
```

If the existing production setup uses another suitable ownership model, keep that model, but the mounted backup directory must remain writable by the SQL Server container.

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
MSSQL_SA_PASSWORD=ChangeThis_StrongPassword123!
DB_NAME=NeftyanikPortalDb
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

The Compose file also mounts a durable SQL backup directory from the host:

```text
/opt/Neftyanik/deploy/backups/sqlserver -> /var/opt/mssql/backup
```

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

The GitHub Actions deployment workflow keeps this same production behavior. It does not migrate on normal web startup. It explicitly runs the one-shot `migrate` service during deployment after a verified SQL backup is created.

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

Typical emergency/manual update flow:

```sh
git fetch --prune origin master
git merge --ff-only origin/master

docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml build web

docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml stop web

docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml --profile migration run --rm migrate

docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml up -d web
```

Before the migration step, take a SQL Server backup and verify it successfully.

The repository now includes reusable deployment scripts:

```sh
deploy/scripts/create-sql-backup.sh
bash deploy/scripts/deploy-production.sh <tested-commit-sha>
```

`deploy-production.sh` is the script used by GitHub Actions over SSH. It acquires a deployment lock, requires a clean tracked Git state on the VPS, deploys the exact tested commit, creates a verified SQL backup, runs the one-shot migration service, waits for container health, checks the public HTTPS `/health` endpoint, and prints `docker compose ps`.

## Automated GitHub Actions deployment flow

The `Deploy production` workflow performs these steps:

1. check out the exact workflow commit;
2. restore dependencies;
3. build Release;
4. run all existing test projects;
5. open an SSH connection to the VPS with host-key verification;
6. upload the checked-in deployment scripts to a temporary server directory;
7. invoke `deploy-production.sh` with `${{ github.sha }}`;
8. remove the temporary uploaded script directory.

The remote deployment script then:

1. acquires a server-side `flock` lock;
2. verifies `/opt/Neftyanik/deploy/.env` exists;
3. verifies the expected Git branch and remote;
4. fails if tracked local changes exist;
5. fetches `origin/master`;
6. verifies the requested commit exists and is a safe fast-forward from the current production commit;
7. ensures `sqlserver` is healthy;
8. creates and verifies a SQL Server backup in `deploy/backups/sqlserver`;
9. fast-forwards the checked-out production branch to the exact tested commit;
10. rebuilds the `web` image used by both `web` and `migrate`;
11. stops `web` only for the migration window;
12. runs the one-shot `migrate` service;
13. starts `web` again and waits for container health;
14. refreshes `caddy` only when the Caddy or Compose configuration changed;
15. performs the public HTTPS `/health` check through Caddy;
16. prints the deployed Git commit and `docker compose ps`.

## First automated deployment verification

Before the first GitHub Actions deployment, verify these items manually on the VPS:

```sh
cd /opt/Neftyanik
git branch --show-current
git remote -v
test -f deploy/.env
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml config >/dev/null
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml up -d sqlserver
docker compose --env-file deploy/.env -f deploy/docker-compose.production.yml ps
```

Then trigger the workflow from GitHub Actions using `Run workflow`.

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

## Emergency manual deployment fallback

If GitHub Actions is unavailable, use SSH and run the checked-in deployment script manually on the VPS with the exact approved commit SHA:

```sh
cd /opt/Neftyanik
bash deploy/scripts/deploy-production.sh <tested-commit-sha>
```

This fallback keeps the same safety rules as the automated workflow:

- it requires a clean tracked Git state;
- it creates a verified SQL backup before changing the application;
- it fast-forwards only to the requested tested commit;
- it uses the one-shot `migrate` service;
- it does not recreate SQL Server or reset the database;
- it does not attempt automatic EF Core migration rollback.
