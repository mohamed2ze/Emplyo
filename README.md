# Emplyo — Employee Management System

<p align="center">
  <img src="assets/Emplyo-banner.png" alt="Emplyo — Employee Management System" width="420" />
</p>

Emplyo is a complete, professional employee management web application built with
**.NET 10 (ASP.NET Core MVC)**, **Entity Framework Core 10**, and
**Microsoft SQL Server**. It provides role-aware dashboards, employee and
organizational management, attendance, leave workflows, lifecycle management,
document storage, announcements, reporting, auditing, and secure account
administration.

> Status: functional and verified. See [Verification](#verification) and
> [Known limitations](#known-limitations).

---

## Table of contents

1. [Feature overview](#feature-overview)
2. [Technology stack](#technology-stack)
3. [Solution architecture](#solution-architecture)
4. [Prerequisites](#prerequisites)
5. [SQL Server setup](#sql-server-setup)
6. [Configuration](#configuration)
7. [Database migrations](#database-migrations)
8. [Administrator provisioning](#administrator-provisioning)
9. [Development data](#development-data)
10. [Running the application](#running-the-application)
11. [Running tests](#running-tests)
12. [Roles and authorization](#roles-and-authorization)
13. [Verification](#verification)
14. [Security notes](#security-notes)
15. [Deployment](#deployment)
16. [Known limitations](#known-limitations)
17. [Further documentation](#further-documentation)

---

## Feature overview

- **Dashboards** tailored to Administrator, HR Manager, and Employee roles using
  real data and charts.
- **Employee management** — rich profiles, search, filters, sorting, server-side
  pagination, status lifecycle, profile photos, and employment history.
- **Departments & positions** — organizational structure, department heads, and
  staff counts.
- **Attendance** — clock in/out, late detection based on a configurable work-day
  start, corrections with audit trail, summaries and reports.
- **Leave management** — configurable leave types, balances, holiday-aware
  working-day calculation, request/approval/rejection/cancellation workflows with
  notifications.
- **Onboarding & offboarding** — checklists, templates, task tracking, and
  controlled termination that preserves history.
- **Announcements** — audience targeting, scheduling, publishing, and archiving.
- **In-app notifications** for leave and workflow events, linking to authorized pages.
- **Document management** — validated uploads, server-generated filenames, storage
  outside the web root, authorized downloads, confidential documents.
- **Reporting & CSV export** — headcount, status, leave utilization, attendance,
  and employee directory, with formula-injection-safe CSV output.
- **Audit logging** of important operations.
- **User & account management** — ASP.NET Core Identity, role assignment, lockout,
  password reset, secure first-administrator provisioning.
- **Administrative settings** — organization name, IANA time zone, work-day start.

## Technology stack

| Area | Technology |
| --- | --- |
| Runtime | .NET 10 (net10.0) |
| Web | ASP.NET Core MVC, Razor, Bootstrap 5, Bootstrap Icons, Chart.js |
| Data | EF Core 10, `Microsoft.EntityFrameworkCore.SqlServer` |
| Database | Microsoft SQL Server (2022/2025 verified) |
| Identity | ASP.NET Core Identity (cookie authentication) |
| Testing | xUnit, `Microsoft.AspNetCore.Mvc.Testing`, EF Core (real SQL Server) |
| Tooling | .NET CLI, `dotnet-ef`, User Secrets, Docker Compose |

## Solution architecture

Emplyo is a **modular monolith** following Clean Architecture principles:

```
Emplyo/
├── Emplyo.sln
├── src/
│   ├── Emplyo.Domain/        # Entities, enums, value objects, exceptions (no EF/ASP.NET)
│   ├── Emplyo.Application/   # Use cases, DTOs, service interfaces, business rules
│   ├── Emplyo.Infrastructure/# EF Core DbContext, Identity, migrations, external services
│   └── Emplyo.Web/           # MVC controllers, Razor views, design system, composition root
├── tests/
│   ├── Emplyo.UnitTests/         # Business-rule unit tests (EF InMemory)
│   └── Emplyo.IntegrationTests/  # Real HTTP + real SQL Server end-to-end tests
├── docs/
├── Dockerfile
├── docker-compose.yml
└── README.md
```

Dependency direction: `Web → Infrastructure → Application → Domain`.
The Domain layer has no dependency on EF Core or ASP.NET Core. See
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for details and design decisions.

## Prerequisites

- Ubuntu Linux (tested on Ubuntu 26.04) — other Linux distributions should work.
- [.NET 10 SDK](https://dotnet.microsoft.com/) (`dotnet --version` → 10.x).
- Docker with an accessible **Microsoft SQL Server** instance (the project was
  developed against an existing container publishing port `1433`).
- `dotnet-ef` CLI tool (see [Database migrations](#database-migrations)).

Verify your environment:

```bash
dotnet --info
dotnet --list-sdks
docker ps --format '{{.Names}}\t{{.Ports}}'
```

## SQL Server setup

Emplyo uses your **existing** SQL Server instance — it does not create or manage
one. The development instance used while building this project was a container
named `mssql` (image `mcr.microsoft.com/mssql/server`) publishing `1433` with a
persistent volume.

1. Confirm the server is running and reachable on `localhost:1433`:

   ```bash
   docker ps --format '{{.Names}}\t{{.Ports}}'
   ```

2. The application connects using the connection string named
   **`DefaultConnection`**. For local development, store it in **User Secrets**
   (never in source control):

   ```bash
   cd src/Emplyo.Web
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
     "Server=localhost,1433;Database=Emplyo;User Id=sa;Password=<your-password>;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True"
   ```

   In deployment, set the environment variable instead (double underscore
   separates configuration sections):

   ```bash
   export ConnectionStrings__DefaultConnection="Server=...;Database=Emplyo;..."
   ```

3. The `Emplyo` database is created by **migrations** (see below); it is not
   created automatically at application startup. Only idempotent **role seeding**
   runs on startup.

**Required database permissions.** For development the `sa` account is
sufficient. In production, create a dedicated login with least privilege:

- `db_ddladmin` and `db_datareader` / `db_datawriter` on the `Emplyo` database
  (sufficient to run migrations and normal operations), **or**
- `db_owner` on `Emplyo` if migrations are applied by the same account.

Do not use `sa` in production. No credentials are stored in source control.

## Configuration

| Setting | Purpose | Default |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string | *(required)* |
| `Storage:DocumentsRoot` | Private document/upload root (outside web root) | `<content-root>/App_Data/documents` |
| `Email:Enabled` | Enable SMTP delivery of notification email | `false` |
| `Email:Host` / `Port` / `UseSsl` / `FromAddress` / `FromName` | SMTP configuration | — |
| `Organization:TimeZone` | IANA time zone used for display/attendance | `UTC` |
| `Bootstrap:Admin*` | (optional) first-admin provisioning via config | — |

At runtime, organization name, time zone, and work-day start are stored in the
`AppSettings` table and edited from **Settings** (Administrator only).

## Database migrations

Apply migrations with the built-in maintenance command (no web server is started):

```bash
# Ensure the dotnet-ef tool is available (one-time)
dotnet tool install --global dotnet-ef   # or: dotnet tool restore

# Apply migrations + ensure roles/reference settings
dotnet run --project src/Emplyo.Web -- migrate
```

Creating a new migration during development:

```bash
dotnet ef migrations add <Name> \
  --project src/Emplyo.Infrastructure \
  --startup-project src/Emplyo.Web \
  --output-dir Persistence/Migrations
```

> Migrations are **never** applied automatically during normal application
> startup. Existing databases are never dropped or recreated automatically.

## Administrator provisioning

There is **no public administrator registration** and **no hardcoded default
password**. Provision the first administrator explicitly:

```bash
cd src/Emplyo.Web
EMPLYO_ADMIN_EMAIL="admin@yourcompany.com" \
EMPLYO_ADMIN_PASSWORD="<strong-unique-password>" \
EMPLYO_ADMIN_NAME="IT Administrator" \
dotnet run --project src/Emplyo.Web -- seed-admin
```

- The command applies pending migrations, then creates the administrator.
- If a user with that email already exists, or any administrator already exists,
  provisioning is skipped to prevent duplicate privileged accounts.
- Credentials are read from environment variables by preference (arguments such
  as `--email`/`--password` are supported but visible in shell history).
- Passwords are never logged.

If email delivery is not configured (the default), password-reset emails are
suppressed; an administrator can reset a user's password from **User Management**.

## Development data

Fictional seed data (departments, positions, employees, leave types, holidays,
sample attendance, sample leave requests, an announcement, and demo accounts) can
be created with:

```bash
EMPLYO_DEV_PASSWORD="Emplyo!Dev2026" \
dotnet run --project src/Emplyo.Web -- seed-dev-data
```

Demo accounts created (development only — **never use in production**). Password:
`Emplyo!Dev2026`

| Email | Role |
| --- | --- |
| `admin@emplyo.local` | Administrator |
| `hr@emplyo.local` | HR Manager |
| `manager@emplyo.local` | Employee (manager of Engineering) |
| `employee@emplyo.local` | Employee |

The seed is idempotent and clearly fictional. A full inventory of the fictional
data and a **step-by-step presentation walkthrough** (useful for demonstrating the
project, e.g. for a training program) is in
[`docs/DEMO.md`](docs/DEMO.md).

## Running the application

```bash
# From the repository root
dotnet run --project src/Emplyo.Web
```

Then browse to the URL printed in the console (e.g. `http://localhost:5080`).
To choose a URL explicitly:

```bash
ASPNETCORE_URLS=http://localhost:5080 dotnet run --project src/Emplyo.Web
```

Health check: `GET /health` returns `200` when SQL Server is reachable.

## Running tests

```bash
# Unit tests (business rules)
dotnet test tests/Emplyo.UnitTests

# Integration tests (real HTTP + real SQL Server)
dotnet test tests/Emplyo.IntegrationTests

# Everything
dotnet test
```

Integration tests use a dedicated database named **`Emplyo_Test`** derived from
`DefaultConnection` (override with `EMPLYO_TEST_CONNECTION`). They apply
migrations and seed their own data; they never touch the application database.

## Roles and authorization

| Area | Administrator | HR Manager | Employee |
| --- | --- | --- | --- |
| Dashboards | Admin | HR | Personal |
| Employees (directory, create/edit) | ✔ | ✔ | own profile only |
| Departments / Positions | ✔ | ✔ | — |
| Attendance (all / corrections) | ✔ | ✔ | own only |
| Leave (all / approve / reject) | ✔ | ✔ | submit & own only |
| Onboarding / Offboarding | ✔ | ✔ | — |
| Announcements (manage) | ✔ | ✔ | view |
| Documents (manage) | ✔ | ✔ | own, non-confidential |
| Reports & CSV export | ✔ | ✔ | — |
| User management | ✔ | — | — |
| Audit logs | ✔ | — | — |
| Settings | ✔ | — | — |

Authorization is enforced **server-side** via policies and resource checks
(ownership, manager scope, confidentiality). Navigation visibility is never the
only control. See [`docs/SECURITY.md`](docs/SECURITY.md).

## Verification

See [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) for the full verification log.
Summary of the verified build/test state:

- `dotnet build` — **succeeded, 0 warnings, 0 errors**.
- `dotnet test tests/Emplyo.UnitTests` — **30 passed**.
- `dotnet test tests/Emplyo.IntegrationTests` — **12 passed** (real SQL Server).
- Manual HTTP checks: login/logout, role-based access enforcement, employee
  creation, duplicate rejection, attendance clock in/out, leave submission and
  approval with balance deduction, cross-employee access denial, and CSV export.

## Security notes

- ASP.NET Core Identity with a 12+ character password policy, lockout after 5
  failed attempts, and secure cookies.
- Global CSRF protection (auto-validated antiforgery token on unsafe verbs).
- Resource-based authorization for employee, leave, attendance, and document access.
- Documents stored outside the web root with server-generated filenames; every
  download is authorized.
- CSV exports protect against spreadsheet formula injection.
- Audit logging excludes passwords and tokens.
- Secrets are kept in User Secrets / environment variables, never in source.
- HTTPS/HSTS enforced outside Development.

## Deployment

See [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md). A multi-stage [`Dockerfile`](Dockerfile)
and an optional [`docker-compose.yml`](docker-compose.yml) are provided. The
compose file does **not** create or own SQL Server by default.

## Known limitations

- Email delivery is optional and disabled by default; password-reset emails are
  suppressed when it is disabled (admin-driven reset is available).
- Leave policy is configurable but does not encode any specific jurisdiction's
  employment law — review policies for your jurisdiction.
- No malware scanning is implemented for uploads (documented, not claimed).
- No real-time push (no SignalR); notifications are pull-based.
- Audit logs are append-only through the application; there is no external WORM
  storage guarantee.
- Partial-day leave is limited to a single half day.

## Further documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/DATABASE.md`](docs/DATABASE.md)
- [`docs/SECURITY.md`](docs/SECURITY.md)
- [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)
- [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md)
- [`docs/DEMO.md`](docs/DEMO.md) — fictional demo data and presentation walkthrough
