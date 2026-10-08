# Deployment

## Overview

Emplyo is a standard ASP.NET Core application. It can run:

- **Directly on a Linux host** (recommended for your Ubuntu environment), or
- **In a container** using the provided `Dockerfile` / `docker-compose.yml`.

In both cases it connects to an existing Microsoft SQL Server instance. Emplyo
does **not** provision or own SQL Server.

## Configuration for production

Provide configuration through environment variables (do not commit secrets):

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__DefaultConnection="Server=<host>,1433;Database=Emplyo;User Id=<user>;Password=<pw>;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True"
export Storage__DocumentsRoot="/var/lib/emplyo/documents"
export Email__Enabled="true"
export Email__Host="smtp.example.com"
export Email__Port="587"
export Email__UseSsl="true"
export Email__UserName="<user>"
export Email__Password="<pw>"
export Email__FromAddress="no-reply@example.com"
export Email__FromName="Emplyo"
```

Set the application's organization settings (name, time zone, work-day start)
from the **Settings** page after the administrator has signed in.

## Database preparation

```bash
# Apply migrations (idempotent)
dotnet run --project src/Emplyo.Web -- migrate

# Provision the first administrator
EMPLYO_ADMIN_EMAIL="admin@example.com" EMPLYO_ADMIN_PASSWORD="<strong-pw>" \
  dotnet run --project src/Emplyo.Web -- seed-admin
```

Recommended production database permissions (least privilege): a dedicated login
with `db_ddladmin` + `db_datareader` + `db_datawriter` on the `Emplyo` database
(use `db_owner` only if the same account applies migrations). Do not use `sa`.

## Running natively (systemd example)

```ini
# /etc/systemd/system/emplyo.service
[Unit]
Description=Emplyo employee management
After=network.target

[Service]
WorkingDirectory=/opt/emplyo
ExecStart=/usr/bin/dotnet /opt/emplyo/Emplyo.Web.dll
Restart=always
RestartSec=5
EnvironmentFile=/etc/emplyo/emplyo.env
User=emplyo

[Install]
WantedBy=multi-user.target
```

Publish the app first:

```bash
dotnet publish src/Emplyo.Web -c Release -o /opt/emplyo
```

Place a reverse proxy (nginx/Caddy) in front and terminate TLS there.

## Running with Docker

The provided compose file runs only the web container and reaches SQL Server via
`host.docker.internal`:

```bash
EMPLYO_DB_PASSWORD='<pw>' docker compose up --build
```

The application listens on `http://localhost:8080` in the container. The compose
file intentionally does **not** define SQL Server, to avoid conflicting with your
existing instance. An isolated SQL Server service is included as a commented
block for environments that truly need one.

Build the image directly if preferred:

```bash
docker build -t emplyo-web:latest .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="Server=host.docker.internal,1433;Database=Emplyo;User Id=<user>;Password=<pw>;TrustServerCertificate=True" \
  --add-host host.docker.internal:host-gateway \
  emplyo-web:latest
```

## Health checks and monitoring

- Liveness/readiness: `GET /health` (checks SQL Server connectivity).
- Use structured logs (console) for aggregation; sensitive values are not logged.

## Hardening checklist

- [ ] `ASPNETCORE_ENVIRONMENT=Production` (enables HTTPS redirection + HSTS).
- [ ] TLS terminated at the proxy or the host.
- [ ] Dedicated least-privilege database login (no `sa`).
- [ ] Secrets provided via environment variables or a secret manager.
- [ ] Persistent volume for `Storage__DocumentsRoot`, backed up with the database.
- [ ] Regular SQL Server backups.
- [ ] First administrator provisioned; default/demo accounts not used.
- [ ] Organization time zone configured in Settings.
- [ ] Email configured (or password resets handled by administrators).

## Upgrades

1. Back up the database and document store.
2. Deploy the new build.
3. Run `dotnet run --project src/Emplyo.Web -- migrate`.
4. Restart the service.

Migrations are forward-only and applied explicitly; they are never run
automatically at startup.
