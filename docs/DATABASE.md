# Database

## Provider and approach

- **Microsoft SQL Server** via `Microsoft.EntityFrameworkCore.SqlServer` (EF Core 10).
- **Code-first** with EF Core migrations.
- The schema is created and evolved only through migrations — never dropped or
  recreated automatically by the application.

## Entities

| Entity | Purpose | Notable constraints |
| --- | --- | --- |
| `ApplicationUser` (Identity) | Account, roles, display name, active flag | inherits `IdentityUser` |
| `Employee` | Employee profile | unique `EmployeeNumber`, unique `WorkEmail`, indexes on department/status; `rowversion` |
| `Department` | Organizational unit | unique `Name`; optional department head |
| `Position` | Job position | unique (`Title`, `DepartmentId`) |
| `LeaveType` | Configurable leave category | unique `Name` |
| `LeaveBalance` | Per-employee, per-type, per-year balance | unique (`EmployeeId`,`LeaveTypeId`,`Year`); `rowversion` |
| `LeaveRequest` | Leave request and decision | indexes on (`EmployeeId`,`Status`), `StartDate`; `rowversion` |
| `AttendanceRecord` | Daily attendance | unique (`EmployeeId`,`WorkDate`); `rowversion` |
| `Holiday` | Company holiday calendar | unique (`Date`,`Name`) |
| `Announcement` | Company announcement | audience targeting; optional schedule/expiry |
| `Notification` | In-app notification | index on (`UserId`,`IsRead`) |
| `EmployeeDocument` | Document metadata | `rowversion` |
| `OnboardingTask` | Onboarding checklist item | cascade with employee |
| `OffboardingCase` / `OffboardingTask` | Offboarding case + checklist | cascade |
| `AuditLog` | Append-only audit events | indexes on timestamp/action/target |
| `AppSetting` | Key/value configuration | unique `Key` |

## Relationships and delete behavior

- `Employee → Department`, `Employee → Position`, `Employee → Manager` use
  `Restrict` to protect historical data.
- `Employee → User` is optional (`SetNull`).
- Leave balances/requests, attendance, documents, onboarding tasks, and
  offboarding cases cascade with their owning employee (children only).
- `LeaveRequest → LeaveType` and `LeaveBalance → LeaveType` use `Restrict`.
- Applications never delete employees during normal operation; termination changes
  status and preserves all records.

## Data types

- Monetary values use `decimal(18,2)` (`Employee.AnnualCompensation`).
- Leave amounts/days use `decimal(6,2)`.
- Dates use `date` (`DateOnly`); timestamps use `datetime2` in UTC.
- Strings have explicit maximum lengths; foreign keys and unique constraints are
  enforced at the database level.
- Concurrency uses SQL Server `rowversion`.

## Indexing and performance

- Unique indexes on natural keys (`EmployeeNumber`, `WorkEmail`, `Department.Name`,
  `LeaveType.Name`, `Holiday`, `LeaveBalance`, `AttendanceRecord`).
- Query indexes on frequently filtered columns (`EmploymentStatus`, `WorkDate`,
  `StartDate`, `IsPublished`, `UserId`+`IsRead`, audit timestamp/action).
- Asynchronous queries with `AsNoTracking` for reads; projections to DTOs avoid
  loading unnecessary columns.
- Server-side pagination everywhere; aggregations push `GROUP BY`/`COUNT` to SQL.
- `EnableRetryOnFailure` for transient SQL Server faults.

## Migrations

The initial migration is `InitialCreate` under
`src/Emplyo.Infrastructure/Persistence/Migrations`.

Apply migrations (also seeds roles and baseline settings):

```bash
dotnet run --project src/Emplyo.Web -- migrate
```

Create a new migration:

```bash
dotnet ef migrations add <Name> \
  --project src/Emplyo.Infrastructure \
  --startup-project src/Emplyo.Web \
  --output-dir Persistence/Migrations
```

## Initialization rules

- The application **never** calls `EnsureCreated`, `Migrate`, or `Drop` during
  ordinary startup.
- Startup seeds roles idempotently and tolerates an unreachable database.
- First administrator provisioning (`seed-admin`) and development seeding
  (`seed-dev-data`) are explicit, idempotent CLI commands.

## Backup and retention

- Backups are the responsibility of the SQL Server operator (e.g. full/diff/log
  backups, or volume snapshots of the container's data volume).
- Soft lifecycle: terminated employees, leave history, and audit records are
  retained. Deletion of employee records is intentionally not exposed in the UI.
- Documents are stored on the filesystem outside the web root; back them up
  alongside database backups.
