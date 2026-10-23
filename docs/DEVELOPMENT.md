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
| All tests | `dotnet test` |
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

## Testing approach

- **Unit tests** (`Emplyo.UnitTests`) cover pure domain logic
  (`WorkingDayCalculator`, `EmploymentStatusPolicy`) and Application services using
  EF Core InMemory with lightweight fakes for cross-cutting interfaces.
- **Integration tests** (`Emplyo.IntegrationTests`) boot the real application with
  `WebApplicationFactory<Program>` against a dedicated **`Emplyo_Test`** SQL Server
  database (derived from `DefaultConnection`, overridable with
  `EMPLYO_TEST_CONNECTION`). They exercise real HTTP, real Identity, real EF Core,
  and real SQL Server constraints. They are isolated and repeatable and never touch
  the application database.

## Verification log

Verification performed during development (Ubuntu, .NET 10.0.112, SQL Server 2025
container on `localhost:1433`):

| Check | Result |
| --- | --- |
| `dotnet build` | Succeeded — 0 warnings, 0 errors |
| `dotnet test tests/Emplyo.UnitTests` | **31 passed**, 0 failed |
| `dotnet test tests/Emplyo.IntegrationTests` | **12 passed**, 0 failed |
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
