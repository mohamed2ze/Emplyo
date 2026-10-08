# Architecture

## Overview

Emplyo is a **modular monolith** built with ASP.NET Core MVC and EF Core. It is
organized into four production projects plus two test projects, with a strict,
one-directional dependency graph:

```
Emplyo.Web  ──▶  Emplyo.Infrastructure  ──▶  Emplyo.Application  ──▶  Emplyo.Domain
```

| Project | Responsibility | Key dependencies |
| --- | --- | --- |
| `Emplyo.Domain` | Entities, enums, value objects (e.g. `WorkingDayCalculator`, `EmploymentStatusPolicy`), domain exceptions. Pure C#. | none |
| `Emplyo.Application` | Use cases / application services, DTOs, abstraction interfaces. Contains business workflows. | Domain, EF Core (abstractions only) |
| `Emplyo.Infrastructure` | EF Core `AppDbContext`, ASP.NET Core Identity, migrations, file storage, audit, settings, clock, email. | Application, EF Core SqlServer, Identity |
| `Emplyo.Web` | MVC controllers, Razor views, view models, design system, filters, composition root (`Program.cs`). | Infrastructure |

Dependency inversion is used for cross-cutting concerns: the Application layer
declares interfaces such as `IApplicationDbContext`, `ICurrentUserService`,
`IAuditService`, `INotificationService`, `IFileStorageService`, `IIdentityService`,
and `IAppSettingsService`; Infrastructure (or the Web layer, for HTTP-specific
concerns such as `ICurrentUserService`) provides the implementations.

## Layer rules

- **Domain** does not reference EF Core or ASP.NET Core.
- **Application** services contain business rules and orchestrate persistence via
  `IApplicationDbContext`. They never reference HTTP or Razor.
- **Infrastructure** implements persistence and external integrations. It is the
  only project that references `Microsoft.EntityFrameworkCore.SqlServer`.
- **Web** handles HTTP, presentation, and request-level authorization. Controllers
  are kept thin and delegate to Application services; strongly typed view models
  are used for all forms (entities are never bound directly).

## Application services

Business workflows live in cohesive services grouped by module:

| Module | Service | Highlights |
| --- | --- | --- |
| Employees | `EmployeeService` | uniqueness of number/email, status transitions, manager/department validation, contact updates |
| Organization | `DepartmentService`, `PositionService` | unique names, staffing counts, valid references |
| Attendance | `AttendanceService` | duplicate clock-in prevention, clock-out validation, late detection, audited corrections |
| Leave | `LeaveService`, `LeaveTypeService`, `HolidayService` | holiday-aware working-day calculation, overlap detection, balance deduction/restoration, self-approval prevention |
| Lifecycle | `LifecycleService` | onboarding templates, offboarding checklists, controlled termination |
| Announcements | `AnnouncementService` | audience targeting, scheduling, publish/archive |
| Documents | `DocumentService` | extension/size/content-type validation, safe storage, audited lifecycle |
| Notifications | `NotificationService` | in-app notifications to users and roles |
| Reporting | `ReportService` | aggregations for reports and CSV export |
| Dashboard | `DashboardService` | role-specific dashboards using real data |
| Users | `UserAdminService` | Identity-backed account administration |
| Auditing | `AuditQueryService` | querying append-only audit events |

## Key design decisions

- **Pragmatic clean architecture.** Rather than adding a mediator/CQRS layer that
  would increase indirection without benefit at this size, the Application layer
  exposes cohesive services. This keeps the code readable while preserving
  separation of concerns.
- **`IApplicationDbContext` abstraction.** The Application layer depends on an
  interface exposing `DbSet<T>` and `SaveChangesAsync`, implemented by
  `AppDbContext`. This avoids generic repositories that duplicate EF Core, while
  keeping infrastructure substitutable and testable.
- **No business logic in views or controllers.** Controllers map HTTP to service
  calls and shape view models; all rules live in Application/Domain.
- **Domain exceptions mapped to HTTP.** `DomainException` (and subclasses
  `BusinessRuleException`, `ConflictException`, `NotFoundException`,
  `ForbiddenException`) are translated by `DomainExceptionFilter` into friendly
  responses (toasts, JSON for AJAX, 404/403 pages) instead of stack traces.
- **Time handling.** Timestamps are persisted in UTC. A configurable IANA time
  zone (`AppSettings`) is used for display and for computing attendance days via
  `TimeZoneProvider`. The server's local time zone is never assumed.
- **Concurrency.** Sensitive entities (`Employee`, `LeaveRequest`, `LeaveBalance`,
  `AttendanceRecord`, `EmployeeDocument`) use SQL Server `rowversion` concurrency
  tokens.
- **Controlled initialization.** Migrations, administrator provisioning, and dev
  seeding are explicit CLI commands. Ordinary startup only performs idempotent
  role seeding.

## Request pipeline

1. `UseExceptionHandler` (production) / `UseDeveloperExceptionPage` (development).
2. `UseStatusCodePagesWithReExecute` → friendly status pages.
3. `UseStaticFiles`, `UseRouting`, `UseAuthentication`, `UseAuthorization`.
4. MVC with global `AutoValidateAntiforgeryToken` and `DomainExceptionFilter`.
5. `MapHealthChecks("/health")` → custom `DatabaseHealthCheck`.

## Extensibility

The module boundaries make it straightforward to add new bounded modules
(e.g. payroll, performance reviews) by adding an entity, a migration, an
Application service, and a controller/views, without modifying unrelated modules.
