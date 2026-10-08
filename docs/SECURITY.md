# Security

This document describes the technical controls implemented in Emplyo. It is not a
claim of legal compliance; organizational and legal review is still required.

## Authentication

- **ASP.NET Core Identity** with cookie authentication.
- Password policy: minimum 12 characters with upper, lower, digit and symbol.
- **Lockout** after 5 failed attempts for 15 minutes.
- Unique email per account.
- Login, logout, forgot-password, reset-password, change-password, and an
  access-denied page are implemented.
- Password reset responses are **neutral** (do not reveal whether an email
  exists); reset tokens are never logged.
- Account activation/deactivation is controlled by administrators; deactivated
  accounts are locked out.
- The **first administrator** is provisioned explicitly via CLI using environment
  variables; there is no public administrator registration and no default password
  in source.

## Session and transport

- Auth cookie: `HttpOnly`, `SameSite=Lax`, `Secure` outside Development, 8-hour
  sliding expiration.
- HTTPS redirection and HSTS enabled outside Development.
- **CSRF**: `AutoValidateAntiforgeryTokenAttribute` is applied globally to unsafe
  HTTP methods; all forms emit and validate antiforgery tokens.

## Authorization

Authorization is enforced server-side. Navigation visibility is never the only
control.

- **Policies**: `RequireAdministrator`, `RequireHrManager`, `RequireHrOrAdmin`,
  `RequireAuthenticated`.
- **Resource-based checks** for:
  - Employee details — staff, the employee themselves, or their manager only.
  - Leave details — staff or the owning employee only.
  - Leave approve/reject — HR/Admin only, and **self-approval is forbidden**.
  - Attendance corrections — HR/Admin only.
  - Document download — staff or owner; confidential documents are staff-only.
- **IDOR protection**: client-supplied IDs are always re-authorized; role, status,
  and approval values are never accepted from the client.
- Sensitive fields (compensation) are hidden from non-privileged views and nulled
  before rendering.

## Input validation and errors

- Data annotations on view models/DTOs plus server-side business-rule validation.
- Database constraints (uniqueness, foreign keys, required fields) as a final line
  of defence.
- Domain exceptions map to friendly responses; stack traces and internal details
  are never shown to ordinary users.
- Structured logging; sensitive values such as passwords and tokens are never logged.

## Documents

- Allowed extensions and content types are allow-listed; size limited to 10 MB.
- Server-generated GUID filenames; client filenames are never trusted. Path
  traversal is prevented by normalizing and confining paths to the storage root.
- Files are stored **outside** the public web root (`App_Data/documents` by
  default).
- Every download is authorized; no predictable public URLs.
- Malware scanning is **not** implemented and is not claimed.

## Audit logging

- Important operations (employee changes, status changes, leave decisions,
  attendance corrections, document changes, user/role changes, settings changes)
  are recorded with actor, action, target, outcome, timestamp, and limited
  metadata.
- Passwords, tokens, and secrets are never recorded.
- Audit logs are restricted to Administrators and are not editable/deletable
  through the application.

## Secrets and configuration

- Connection strings and credentials live in **User Secrets** (development) or
  environment variables (deployment), never in source control.
- The `.gitignore` excludes secrets, `App_Data`, uploads, and build output.

## Privacy

- Only information needed for defined business purposes is collected.
- Least privilege is applied via roles and resource checks.
- Sensitive data is not placed in URLs.
- No third-party data transmission occurs; email is optional and configurable.
- No GPS/biometric monitoring is implemented.
- **GDPR note**: Emplyo provides technical controls (access control, audit,
  confidentiality) that can support GDPR obligations, but data-retention periods,
  lawful bases, data-subject rights processes, and records of processing require
  organizational and legal review. This software does not by itself constitute
  legal compliance.
