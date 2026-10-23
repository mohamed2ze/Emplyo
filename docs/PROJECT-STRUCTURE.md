# Project structure

This is the reference for how the repository is organized: what each directory
owns, where new code belongs, which files are generated, and the naming
conventions to follow. For the reasoning behind the layering, see
[ARCHITECTURE.md](ARCHITECTURE.md); for build/test commands, see
[DEVELOPMENT.md](DEVELOPMENT.md).

## Repository layout

```
Emplyo/
├── Emplyo.sln                     # Solution: 4 production projects + 2 test projects
├── src/
│   ├── Emplyo.Domain/              # Entities, enums, value objects, exceptions — pure C#
│   ├── Emplyo.Application/        # Use cases: DTOs, service interfaces, services
│   ├── Emplyo.Infrastructure/     # EF Core DbContext, Identity, migrations, integrations
│   └── Emplyo.Web/                # MVC controllers, Razor views, composition root
├── tests/
│   ├── Emplyo.UnitTests/           # Business-rule tests (EF InMemory + fakes)
│   ├── Emplyo.IntegrationTests/    # Real HTTP + real SQL Server tests
│   └── Emplyo.E2ETests/            # Real Chromium browser smoke tests
├── docs/                           # All project documentation (see docs/README.md)
├── assets/                         # Branding source assets (README banner, icon source)
├── scripts/
│   └── dev.sh                      # SQL Server container helper + run wrapper
├── .vscode/                        # Workspace-shared VS Code launch/build tasks
├── Dockerfile                      # Multi-stage container image (hardened, non-root)
└── docker-compose.yml              # Optional web-container deployment
```

Dependency direction (never reversed):

```
Emplyo.Web ──▶ Emplyo.Infrastructure ──▶ Emplyo.Application ──▶ Emplyo.Domain
```

## Production projects

### `src/Emplyo.Domain`

Pure C# — no EF Core, no ASP.NET Core references.

| Folder | Contents |
| --- | --- |
| `Entities/` | One file per entity (`Employee`, `LeaveRequest`, …) |
| `Enums/` | All enumerations (`Enums.cs`) |
| `ValueObjects/` | `WorkingDayCalculator`, `EmploymentStatusPolicy` |
| `Exceptions/` | `DomainException` hierarchy (`BusinessRule`, `Conflict`, `NotFound`, `Forbidden`) |

### `src/Emplyo.Application`

One folder per business module, named after the module in PascalCase
(`Employees`, `LeaveManagement`, `Organization`, `Attendance`, `Lifecycle`,
`Announcements`, `Documents`, `Notifications`, `Reporting`, `Dashboard`,
`Auditing`, `Users`).

Each module folder contains:

- `<Module>Dtos.cs` — the module's DTOs **and its service interface**
  (e.g. `ILeaveService` lives in `LeaveDtos.cs`).
- `<Module>Service.cs` — the implementation (or more than one, e.g.
  `LeaveTypeAndHolidayServices.cs`).

Shared cross-cutting abstractions live in `Common/`:

- `Common/Interfaces/` — `IApplicationDbContext`, `ICurrentUserService`,
  `IIdentityService`, `IAuditService`, `INotificationService`,
  `IFileStorageService`, `IAppSettingsService`, `IAppEmailSender`,
  `IDateTimeProvider`
- `Common/Models/` — `PagedResult`, `AuditEntry`

`DependencyInjection.cs` registers every module service — a new service must be
added there.

### `src/Emplyo.Infrastructure`

| Folder | Contents |
| --- | --- |
| `Persistence/` | `AppDbContext`, `DatabaseInitializer` (role seeding, admin/dev-data commands) |
| `Persistence/Migrations/` | **Generated** EF Core migrations (committed intentionally) |
| `Identity/` | `ApplicationUser` |
| `Services/` | Implementations of Application abstractions (`FileStorageService`, `IdentityService`, `AuditService`, `AppSettingsService`, `SystemClock`, `LoggingEmailSender`) |

It is the only project referencing `Microsoft.EntityFrameworkCore.SqlServer`.

### `src/Emplyo.Web`

The composition root and presentation layer.

| Folder | Contents |
| --- | --- |
| `Controllers/` | One controller per feature area (thin; delegate to Application services) |
| `Views/` | `Views/<Controller>/<Action>.cshtml` + `Views/Shared/` partials/layouts |
| `ViewModels/` | Strongly typed form/view models for complex pages (entities are never bound directly) |
| `Services/` | HTTP-specific services: `CurrentUserService`, `TimeZoneProvider`, `Csv`, `Ui`, `CliCommands`, `ApplicationUserClaimsPrincipalFactory` |
| `Authorization/` | `Roles` and `Policies` constants |
| `Filters/` | `DomainExceptionFilter` (domain exceptions → friendly responses) |
| `HealthChecks/` | `DatabaseHealthCheck` (`GET /health`) |
| `wwwroot/` | `css/emplyo.css`, `js/emplyo.js`, `img/`, `lib/` |

Entry points:

- `Program.cs` — composition root; builds the app, wires Identity, policies,
  filters, and the request pipeline.
- `Services/CliCommands.cs` — maintenance commands (`migrate`, `seed-admin`,
  `seed-dev-data`) run via `dotnet run --project src/Emplyo.Web -- <command>`.

## Tests

| Project | Scope | Layout |
| --- | --- | --- |
| `Emplyo.UnitTests` | Domain + Application business rules, plus infrastructure helpers | One file per area (`LeaveServiceTests`, …) + `TestSupport/` fakes |
| `Emplyo.IntegrationTests` | Real HTTP, real Identity, real SQL Server, database constraints | `EmplyoWebAppFactory` (boots the app against the `Emplyo_Test` database) + `TestInfrastructure` helpers + test classes |
| `Emplyo.E2ETests` | Real Chromium browser through Playwright against the running app | `EmplyoE2EFixture` (starts Kestrel, migrates/seeds `Emplyo_E2E`) + workflow tests |

Integration tests use a dedicated **`Emplyo_Test`** database derived from
`DefaultConnection` (override with `EMPLYO_TEST_CONNECTION`). The database is
migrated and seeded but **never reset**, so tests must tolerate data left by
previous runs. Browser tests use a separate **`Emplyo_E2E`** database with fixed
test-only credentials.

## Where new code belongs

To add a feature (e.g. a new business module):

1. Entity (if needed) → `src/Emplyo.Domain/Entities/<Name>.cs`
2. Migration → `dotnet ef migrations add <Name> --project src/Emplyo.Infrastructure --startup-project src/Emplyo.Web --output-dir Persistence/Migrations`
3. DTOs + service interface + service → `src/Emplyo.Application/<Module>/`
4. DI registration → `src/Emplyo.Application/DependencyInjection.cs`
5. Controller + views → `src/Emplyo.Web/Controllers/`, `src/Emplyo.Web/Views/<Controller>/`
6. Unit tests → `tests/Emplyo.UnitTests/<Module>ServiceTests.cs`
7. Integration tests → `tests/Emplyo.IntegrationTests/`
8. Browser-level flows → `tests/Emplyo.E2ETests/`

Rules to respect: no business logic in controllers or views; the Domain layer
stays free of framework references; no automatic or destructive database
operations at startup. See [ARCHITECTURE.md](ARCHITECTURE.md).

## Assets and static files

- `assets/` — high-resolution **branding sources** (README banner, 1254×1254
  icon source). Not part of the build.
- `src/Emplyo.Web/wwwroot/img/` — optimized runtime copies of the branding
  (e.g. 512×512 icon). Used by the application.
- `src/Emplyo.Web/wwwroot/lib/` — **vendored, intentionally committed**
  frontend libraries (Bootstrap, Bootstrap Icons, jQuery + validation, Chart.js,
  self-hosted Inter font). Never edit by hand; update by replacing the vendored
  copy deliberately.
- `src/Emplyo.Web/wwwroot/css/emplyo.css` — the hand-written design system
  implementing the tokens defined in [DESIGN.md](DESIGN.md).

## Generated and never-edited files

| Path | Generated by | Committed? |
| --- | --- | --- |
| `src/Emplyo.Infrastructure/Persistence/Migrations/*` | `dotnet-ef` | Yes — schema history |
| `src/Emplyo.Web/wwwroot/lib/*` | Vendored upstream releases | Yes — self-hosting by design |
| `src/*/bin/`, `src/*/obj/` | Build | No (ignored) |
| `src/Emplyo.Web/App_Data/` | Runtime (uploaded documents) | No (ignored) |

## Naming conventions

- Application modules: PascalCase folder, `<Module>Dtos.cs` + `<Module>Service.cs`;
  DTOs end in `Dto` (`EmployeeDetailDto`), list items in `ListItemDto`, forms in
  `FormDto`, query inputs in `Filter`.
- Controllers: one per feature area, pluralized where grammatical
  (`EmployeesController`, `LeaveController` — uncountable nouns stay singular).
- Views: match the controller name and action exactly.
- Tests: `<Subject>Tests.cs`; integration test classes describe workflows.
- Constants for roles/policies live in `src/Emplyo.Web/Authorization/` — never
  hardcode role strings elsewhere.
