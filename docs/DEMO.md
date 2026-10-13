# Emplyo — Demo & Presentation Guide

This guide contains the **fictional demonstration data** and a suggested
walkthrough you can use when presenting Emplyo (for example, as a training-program
project). All names, emails, and records below are **fictional and for
demonstration only** — they are not real people or data.

> The application is normally accessed at `http://localhost:5076` when started
> with `dotnet run --project src/Emplyo.Web`.

---

## 1. Start and seed the demo data

If the database is empty, apply migrations and seed the fictional data:

```bash
dotnet run --project src/Emplyo.Web -- migrate

EMPLYO_DEV_PASSWORD='Emplyo!Dev2026' \
  dotnet run --project src/Emplyo.Web -- seed-dev-data
```

The seed is **idempotent** (running it again does not duplicate records).

## 2. Demo accounts

All demo accounts share the password supplied through `EMPLYO_DEV_PASSWORD`
(`Emplyo!Dev2026` in the examples below).

| Email | Role | Linked employee |
| --- | --- | --- |
| `admin@emplyo.local` | **Administrator** | — |
| `hr@emplyo.local` | **HR Manager** | Amina Yusuf (EMP-0001) |
| `manager@emplyo.local` | **Employee** | Daniel Okafor (EMP-0002) |
| `employee@emplyo.local` | **Employee** | Lena Haddad (EMP-0003) |

> These credentials exist **only** for development/demonstration. Never use them
> in a real deployment. Provision a real administrator with `seed-admin`.

## 3. Fictional data inventory

### Departments
Engineering · Sales · Customer Support · Finance · Human Resources

### Positions
Engineering Manager · Senior Software Engineer · Software Engineer · QA Engineer ·
Sales Manager · Account Executive · Sales Representative · Support Team Lead ·
Support Specialist · Finance Manager · Accountant · HR Manager · HR Specialist

### Employees

| Employee # | Name | Department | Position | Status |
| --- | --- | --- | --- | --- |
| EMP-0001 | Amina Yusuf | Human Resources | HR Manager | Active |
| EMP-0002 | Daniel Okafor | Engineering | Engineering Manager | Active |
| EMP-0003 | Lena Haddad | Engineering | Software Engineer | Active |
| EMP-0004 | Marcus Berg | Finance | Finance Manager | Active |
| EMP-0005 | Priya Nair | Sales | Sales Manager | Active |
| EMP-0006 | Tomas Novak | Engineering | QA Engineer | Pending (onboarding) |
| EMP-0007 | Sofia Reyes | Customer Support | Support Team Lead | Active |
| EMP-0008 | Hassan Farouk | Engineering | Senior Software Engineer | On leave |
| EMP-0009 | Grace Kim | Human Resources | HR Specialist | Active |
| EMP-0010 | Oliver Smith | Engineering | Software Engineer | Active |
| EMP-0011 | Fatima Zahra | Finance | Accountant | Active |
| EMP-0012 | Nikolai Petrov | Engineering | Software Engineer | Active |
| EMP-0013 | Aisha Bello | Customer Support | Support Specialist | Active (intern) |
| EMP-0014 | Jakub Kowalski | Engineering | Software Engineer | Active |
| EMP-0015 | Mei Chen | Sales | Sales Representative | Active (contract) |
| EMP-0016 | Carlos Mendes | Sales | Account Executive | Active |
| EMP-0017 | Ingrid Larsen | Customer Support | Support Specialist | Active (part time) |
| EMP-0018 | Youssef Mansour | Engineering | Software Engineer | Active |
| EMP-0019 | Elena Popescu | Sales | Sales Representative | Active |
| EMP-0020 | Ravi Sharma | Engineering | Software Engineer | Terminated |

Each department has a department head, and employees report to their department
head. Hire dates span 2018–2026. Some optional fields (phone, address, emergency
contact, compensation) are intentionally left blank, as in a real directory.

### Leave types
| Leave type | Annual entitlement | Paid |
| --- | --- | --- |
| Annual Leave | 20 days | Yes |
| Sick Leave | 10 days | Yes |
| Parental Leave | 5 days | Yes |
| Unpaid Leave | — | No |

### Holidays (current year)
New Year's Day · Founders' Day · Summer Bank Holiday · Year-end Closure

### Sample leave requests (for the workflow demo)
| Employee | Type | Status | Notes |
| --- | --- | --- | --- |
| Hassan Farouk (EMP-0008) | Annual Leave | **Approved** | Covers the current week; matches his On leave status |
| Lena Haddad (EMP-0003) | Annual Leave | **Approved** | 5 working days next week; her Annual Leave balance shows the deduction |
| Daniel Okafor (EMP-0002) | Sick Leave | **Pending** | 2 working days; appears in the HR approval queue |
| Aisha Bello (EMP-0013) | Annual Leave | **Pending** | 3 working days; second item in the approval queue |
| Oliver Smith (EMP-0010) | Annual Leave | **Rejected** | Decision comment explains why |

### Other seeded data
- ~10 days of sample attendance records per active employee, including remote
  Fridays for employees whose work location is Remote.
- Onboarding checklist for Tomas Novak: one completed task, one in progress, two
  pending.
- A completed offboarding case for Ravi Sharma with four completed tasks.
- Two published announcements: head office maintenance and the performance review
  cycle.
- Leave balances for the current year for every employee.

## 4. Suggested presentation walkthrough

### Step 1 — Employee experience (`employee@emplyo.local`)
1. Sign in and show the **personal dashboard**: status, remaining leave days,
   today's clock-in, recent attendance, leave balances, announcements.
2. **Clock in** (and clock out) from the dashboard.
3. Open **My attendance** to show history and the summary.
4. Open **My leave** → **Request leave**: pick Annual Leave, a future date range,
   and submit. Point out that weekends/holidays are excluded and balances are shown.
5. Open the new request's **details** to show the pending state.
6. Show **Documents** and **Announcements**.

### Step 2 — HR experience (`hr@emplyo.local`)
1. Sign in and show the **HR dashboard**: headcount, pending leave, attendance today,
   department distribution chart, recent hires, upcoming milestones.
2. Open **Leave management** → filter **Pending**. Approve or reject Daniel's Sick
   Leave request. Point out that the approved requests already deducted balances.
3. Open **Employees** and demonstrate **search, filters, sorting and pagination**.
4. Open an employee → show the profile, restricted compensation field, attendance,
   leave history, and documents.
5. Open **Onboarding** → **Tomas Novak** and complete a checklist task; show the
   progress bar.
6. Open **Offboarding** to show the completed case for Ravi Sharma and the
   controlled-termination flow.
7. Open **Reports** → show headcount, leave utilization, attendance report, and
   click a **CSV export**.

### Step 3 — Administrator experience (`admin@emplyo.local`)
1. Sign in and show the **Administrator dashboard**: totals, status chart, recent
   activity, upcoming holidays.
2. **User management**: show the four accounts and role assignment; open the
   **Reset password** screen (without saving).
3. **Audit logs**: show recorded events (employee changes, leave decisions, logins).
4. **Settings**: show organization name, **time zone**, and work-day start.
5. **Leave types** and **Holidays**: show configuration of leave policies and the
   holiday calendar.
6. Optionally show **Departments/Positions** management.

### Step 4 — Security talking points
- Roles are enforced **server-side**; navigation hiding is not the control.
- An employee cannot open another employee's leave request (resource authorization).
- Self-approval of leave is blocked.
- Sensitive fields (compensation) are hidden from non-privileged users.
- CSRF protection, secure cookies, lockout, and audit logging are enabled.
- Documents are stored outside the web root and every download is authorized.

## 5. Resetting the demo data

To return to the state described above (fictional data only), remove the workflow
records and re-seed:

```bash
# Run against the local Emplyo database with a SQL client
DELETE FROM LeaveRequests;
UPDATE LeaveBalances SET Used = 0;

EMPLYO_DEV_PASSWORD='Emplyo!Dev2026' \
  dotnet run --project src/Emplyo.Web -- seed-dev-data
```

> Never run destructive statements against a production database. The commands
> above are intended for the local development database only.

## 6. Tests you can mention

```bash
dotnet test tests/Emplyo.UnitTests         # 31 business-rule tests
dotnet test tests/Emplyo.IntegrationTests  # 12 end-to-end tests (real SQL Server)
```

See [`../README.md`](../README.md) and the other documents in `docs/` for setup,
architecture, database, security, and deployment details.
