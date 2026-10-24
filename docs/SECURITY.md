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
  exists); reset tokens are never logged. Invalid, expired, and unknown-account
  reset submissions return the same generic message.
- Failed sign-in responses are identical for unknown, invalid-password, and
  locked-out accounts, so the lockout state cannot be used for account
  enumeration.
- **Authentication rate limiting**: login, forgot-password, and reset-password
  POSTs are limited per client IP (`Security:AuthRateLimitPerMinute`, default
  30 per minute). Account lockout (5 failures / 15 minutes) remains the
  per-account control.
- Password reset links are built from the configured `App:PublicBaseUrl`
  (required in non-development environments when email is enabled) instead of the
  request `Host` header, preventing reset-link host-header poisoning. Links
  require HTTPS outside Development.
- Account activation/deactivation is controlled by administrators; deactivated
  accounts are locked out, and role/activation/employee-link changes invalidate
  existing sessions through the Identity security stamp. Stamps are revalidated
  every 5 minutes by default (`Security:SecurityStampValidationMinutes`).
- The **first administrator** is provisioned explicitly via CLI using environment
  variables; there is no public administrator registration and no default password
  in source. The application refuses to remove or deactivate the last active
  administrator account.
- Development demo data (`seed-dev-data`) is refused outside the Development
  environment unless `EMPLYO_ALLOW_DEV_SEED=true` is explicitly set.

## Session and transport

- Auth cookie: `HttpOnly`, `SameSite=Lax`, `Secure` outside Development, 8-hour
  sliding expiration. The antiforgery cookie uses the same transport protections.
- HTTPS redirection and HSTS (1 year, include subdomains) enabled outside
  Development.
- `AllowedHosts` defaults to `localhost;127.0.0.1`; production deployments must
  list their real host name(s). Forwarded headers are honored only from
  loopback/configured `Security:KnownProxies`.
- **CSRF**: `AutoValidateAntiforgeryTokenAttribute` is applied globally to unsafe
  HTTP methods; all forms emit and validate antiforgery tokens.
- **Security headers**: `Content-Security-Policy` (same-origin scripts and
  connections, inline style attributes permitted for the design system, no
  framing, no objects), `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`,
  `Cross-Origin-Opener-Policy`, and `Cross-Origin-Resource-Policy`.
- Sensitive responses (`/Account/*`, document downloads, and authenticated
  pages) are sent with `Cache-Control: no-store`; authentication and password
  reset pages use `Referrer-Policy: no-referrer` to avoid leaking reset tokens.
- Request-body limits: Kestrel 16 MB, multipart forms 12 MB, and individual form
  values 1 MB.

## Authorization

Authorization is enforced server-side. Navigation visibility is never the only
control.

- **Policies**: `RequireAdministrator`, `RequireHrManager`, `RequireHrOrAdmin`,
  `RequireAuthenticated`.
- **Resource-based checks** for:
  - Employee details and profile photos — staff, the employee themselves, or
    their direct manager only.
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

- Allowed extensions are allow-listed; size limited to 10 MB. The served content
  type is derived from the validated extension, not from client-supplied MIME
  data.
- Profile photos are limited to 4 MB and validated by extension **and file
  signature** (JPEG/PNG/WebP); HTML or other content disguised as an image is
  rejected.
- Server-generated GUID filenames; client filenames are never trusted. Path
  traversal is prevented by normalizing and confining paths to the storage root.
- Files are stored **outside** the public web root (`App_Data/documents` by
  default).
- Every download is authorized; no predictable public URLs. Confidential
  documents are staff-only.
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
- Password reset tokens appear only in one-time reset links and are protected by
  `Referrer-Policy: no-referrer` and `Cache-Control: no-store`; other sensitive
  data is not placed in URLs.
- No third-party data transmission occurs; email is optional and configurable.
- No GPS/biometric monitoring is implemented.
- **GDPR note**: Emplyo provides technical controls (access control, audit,
  confidentiality) that can support GDPR obligations, but data-retention periods,
  lawful bases, data-subject rights processes, and records of processing require
  organizational and legal review. This software does not by itself constitute
  legal compliance.

## Known limitations and residual risks

These are documented rather than claimed as solved:

- Multi-factor authentication is **not** implemented.
- Malware scanning and content disarm/reconstruction for uploaded documents are
  **not** implemented.
- Role revocation is immediate for new requests after the security stamp changes,
  but an already-issued cookie is revalidated at most every 5 minutes
  (configurable).
- Authentication rate limiting is partitioned by the directly observed client IP.
  Behind a reverse proxy, configure forwarded headers/trusted proxies or apply
  limits at the proxy; otherwise clients may share a partition.
- The CSP allows inline style attributes (`style-src 'unsafe-inline'`) because the
  design system uses inline width styles. Scripts are restricted to same-origin.
- No SAST, DAST, container-image scanning, or SBOM generation is configured in
  CI; dependency auditing is configured. See
  [security/SECURITY_AUDIT.md](security/SECURITY_AUDIT.md).
- TLS termination, SMTP TLS, database transport encryption, database least
  privilege, backups, and secret rotation remain deployment responsibilities.
  See [DEPLOYMENT.md](DEPLOYMENT.md).
