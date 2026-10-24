# Development guide

## Environment

- Ubuntu Linux (developed on Ubuntu 26.04).
- .NET SDK 10 (`10.0.112` used during development).
- An existing Microsoft SQL Server reachable at `localhost:1433` (Docker) — Emplyo
  does not create or manage it.

## First-time setup

```bash
# 1. Restore and build
dotnet restore
dotnet build

# 2. Configure the connection string (User Secrets; never commit)
cd src/Emplyo.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Server=localhost,1433;Database=Emplyo;User Id=sa;Password=<pw>;TrustServerCertificate=True;MultipleActiveResultSets=True;Encrypt=True"
cd ../..

# 3. Install EF Core CLI (if needed)
dotnet tool install --global dotnet-ef    # ensure ~/.dotnet/tools is on PATH

# 4. Apply migrations
dotnet run --project src/Emplyo.Web -- migrate

# 5. Provision the first administrator
EMPLYO_ADMIN_EMAIL="admin@yourcompany.com" EMPLYO_ADMIN_PASSWORD="<strong-pw>" \
  dotnet run --project src/Emplyo.Web -- seed-admin

# 6. (Optional) seed fictional development data
EMPLYO_DEV_PASSWORD="Emplyo!Dev2026" dotnet run --project src/Emplyo.Web -- seed-dev-data
# Demo accounts and a presentation walkthrough: see docs/DEMO.md

# 7. Run
./scripts/dev.sh    # starts the mssql container if needed, then runs the app
```

## Common commands

| Task | Command |
| --- | --- |
| Build | `dotnet build` |
| Run | `./scripts/dev.sh` (ensures the `mssql` container is up first) or `dotnet run --project src/Emplyo.Web` |
| Apply migrations | `dotnet run --project src/Emplyo.Web -- migrate` |
| Add migration | `dotnet ef migrations add <Name> --project src/Emplyo.Infrastructure --startup-project src/Emplyo.Web --output-dir Persistence/Migrations` |
| Unit tests | `dotnet test tests/Emplyo.UnitTests` |
| Integration tests | `dotnet test tests/Emplyo.IntegrationTests` |
| Browser smoke tests | `dotnet test tests/Emplyo.E2ETests` |
| All tests | `dotnet test` |
| Install Playwright Chromium | `pwsh tests/Emplyo.E2ETests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium` |
| Unit coverage | `dotnet test tests/Emplyo.UnitTests --collect:"XPlat Code Coverage" --results-directory TestResults/unit` |
| Integration coverage | `dotnet test tests/Emplyo.IntegrationTests --collect:"XPlat Code Coverage" --results-directory TestResults/integration` |
| Coverage gate | `./scripts/coverage.sh` |
| Vulnerable dependencies | `dotnet list Emplyo.sln package --vulnerable --include-transitive` |
| Health check | `curl http://localhost:5076/health` |
| Provision admin | `EMPLYO_ADMIN_EMAIL=... EMPLYO_ADMIN_PASSWORD=... dotnet run --project src/Emplyo.Web -- seed-admin` |
| Seed dev data | `EMPLYO_DEV_PASSWORD=... dotnet run --project src/Emplyo.Web -- seed-dev-data` |

`scripts/dev.sh` is a thin wrapper: it starts the SQL Server container if it is
stopped, waits until the engine is ready to accept logins, and then executes
`dotnet run --project src/Emplyo.Web -- "$@"`. Any arguments are forwarded, so
`./scripts/dev.sh migrate` runs a maintenance command. Override the container
name or port with `EMPLYO_SQL_CONTAINER` / `EMPLYO_SQL_PORT`, or set
`EMPLYO_SKIP_DB_CHECK=1` when your SQL Server is not managed by Docker. The
VS Code launch configuration runs it automatically before starting the debugger.

## Maintenance commands

`dotnet run --project src/Emplyo.Web -- <command>` supports:

- `migrate` — apply migrations, ensure roles and baseline settings.
- `seed-admin` — provision the first administrator (idempotent, guarded).
- `seed-dev-data` — insert fictional development data (idempotent).
- `help` — show command help.

Commands exit with a non-zero status when required credentials or seed passwords
are missing, so deployment or automation jobs fail visibly instead of silently.

## Testing approach

- **Unit tests** (`Emplyo.UnitTests`) cover domain logic (`WorkingDayCalculator`,
  `EmploymentStatusPolicy`), Application services using EF Core InMemory with
  lightweight fakes for cross-cutting interfaces, and security-sensitive helpers
  such as CSV escaping and file storage. Announcement visibility, leave balance and
  holiday rules, attendance time-zone handling, document validation, notification
  ownership, user-administration consistency, reporting aggregates, and paging
  boundaries all have focused tests.
- **Integration tests** (`Emplyo.IntegrationTests`) boot the real application with
  `WebApplicationFactory<Program>` against a dedicated **`Emplyo_Test`** SQL Server
  database (derived from `DefaultConnection`, overridable with
  `EMPLYO_TEST_CONNECTION`). They exercise real HTTP, real Identity, real EF Core,
  and real SQL Server constraints. They are isolated and repeatable and never touch
  the application database. They also verify resource-based authorization,
  announcement audience enforcement, confidential-document download rules, CSRF
  rejection, account lockout, safe error redirects, security headers, photo and
  document authorization, password-reset host-header handling and enumeration
  resistance, authentication rate limiting, session invalidation after role
  changes, production-safe demo seeding, and CSV export safety.
- **Browser smoke tests** (`Emplyo.E2ETests`) launch the real application on Kestrel
  and drive Chromium through Playwright. The fixture migrates and seeds a dedicated
  **`Emplyo_E2E`** database derived from the configured test connection, then covers
  login, personal navigation, access-denied behavior, CSV download, settings access,
  leave submission/cancellation, and basic accessibility structure. Install the
  Chromium browser once with the command in the table above.
- **Coverage** uses the existing `coverlet.collector` package and emits Cobertura
  reports; `scripts/coverage.sh` merges the unit and integration reports and
  enforces the ratchet (defaults: 57% lines, 63% branches; override with
  `EMPLYO_MIN_LINE_COVERAGE` / `EMPLYO_MIN_BRANCH_COVERAGE`). Coverage is a
  diagnostic, not a guarantee of correctness.
- **Dependency security** is checked with the .NET SDK's built-in vulnerable-package
  audit; the CI job fails if a vulnerable NuGet package is reported.
- **CI** (`.github/workflows/ci.yml`) builds with zero warnings, runs the
  vulnerability audit, enforces the coverage gate, and runs unit, integration, and
  browser suites against a SQL Server service container. Coverage artifacts are
  uploaded. The pipeline is reproducible from the repository without local secrets.

## Verification log

Verification performed during development (Ubuntu, .NET 10.0.112, SQL Server 2025
container on `localhost:1433`):

| Check | Result |
| --- | --- |
| `dotnet build` | Succeeded — 0 warnings, 0 errors |
| `dotnet build -c Release` | Succeeded — 0 warnings, 0 errors |
| `dotnet test tests/Emplyo.UnitTests` | **239 passed**, 0 failed |
| `dotnet test tests/Emplyo.IntegrationTests` | **90 passed**, 0 failed |
| `dotnet test tests/Emplyo.E2ETests` | **7 passed**, 0 failed |
| Coverage gate (unit + integration, union) | 59.59% lines, 68.50% branches |
| `dotnet list Emplyo.sln package --vulnerable --include-transitive` | No vulnerable packages reported |
| `InitialCreate` migration applied to SQL Server | Succeeded |
| `seed-dev-data` | Departments, positions, employees, leave types, holidays, attendance, announcements, demo users |
| App startup + `GET /health` | `200` |
| Anonymous access to `/Employees` | Redirect to `/Account/Login` |
| Administrator login + all admin pages | `200` |
| HR manager restrictions (`/Users`, `/Settings`, `/Audit`) | Denied |
| Employee restrictions (`/Employees`, `/Reports`) | Denied |
| Employee personal pages (`/`, `/Attendance/My`, `/Leave/My`, `/Documents/My`, `/Announcements`) | `200` |
| Employee→manager leave cross-access | Denied (AccessDenied) |
| Leave submit → HR approve → balance deduction | 3 days approved; `Used` increased by 3 |
| Employee creation + duplicate rejection | Created once; duplicate rejected |
| Attendance clock in / clock out | JSON success; duplicate/invalid rejected |
| CSV export (employee directory, headcount) | `200 text/csv`, formula-injection-safe |

## Notes for contributors

- Keep the Domain layer free of EF Core / ASP.NET Core references.
- Add new business rules in Application services and cover them with unit tests.
- Do not add automatic migrations or destructive database operations to startup.
- Run `dotnet build` and both test suites before considering a change complete.
