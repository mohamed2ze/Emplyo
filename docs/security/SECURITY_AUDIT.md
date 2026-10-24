# Security audit — Emplyo

- **Date:** 2026-10-24
- **Repository:** Emplyo (ASP.NET Core MVC / EF Core / SQL Server)
- **Baseline revision:** `99355f2` (`main`), plus the uncommitted remediation
  changes described below
- **Assessment type:** Source-assisted application security review, threat
  modelling, targeted dynamic verification, and remediation

> This report records the state of the repository at the time of the audit. It is
> not a certification, a penetration test, or proof that no vulnerabilities
> remain. Security is an ongoing process.

---

## Executive summary

The assessed application is a modular ASP.NET Core MVC monolith with four
production projects (`Domain`, `Application`, `Infrastructure`, `Web`), ASP.NET
Core Identity cookie authentication, three roles (Administrator, HR Manager,
Employee), EF Core/SQL Server persistence, an authenticated document store, and
optional SMTP email. No public API, OAuth/OIDC integration, webhook receiver,
multi-tenant boundary, or background-processing service is present.

The most serious issue found was **password-reset host-header poisoning**: the
reset link was built from the request `Host` header while `AllowedHosts` was `*`,
so a request with an attacker-controlled host could cause a victim to receive a
reset link pointing at an attacker-controlled origin. This was fixed by requiring
a configured public HTTPS base URL for reset links and refusing to send links
when it is missing or invalid outside Development.

Other confirmed issues were an employee-photo IDOR, account enumeration through
the reset flow and lockout message, missing browser-security headers/CSP, no rate
limiting on authentication endpoints, delayed session invalidation after
role/activation changes, a crafted-referer redirect edge case, client-controlled
document MIME types, weak profile-photo validation, missing request-size limits,
production-unsafe demo seeding, and removal of the last administrator.

All confirmed issues were remediated in the repository. Security regression tests
were added for the high-value fixes. Verification after remediation:

- `dotnet build` (Debug and Release) — succeeded, 0 warnings, 0 errors.
- Unit tests — **239 passed**, 0 failed.
- Integration tests (real SQL Server + HTTP) — **90 passed**, 0 failed.
- Browser tests (Chromium/Playwright) — **7 passed**, 0 failed.
- Release configurations of all three suites — same results.
- Coverage gate — **59.59% lines / 68.50% branches**, passed.
- NuGet vulnerability audit — **no vulnerable packages reported**.

Remaining high-priority risks are primarily operational: no MFA, no malware
scanning for uploaded documents, the residual 5-minute session-revalidation
window, production reverse-proxy/TLS/database hardening, and the absence of
SAST/DAST, container-image scanning, SBOM generation, and centralized security
monitoring. These require deployment or tooling decisions and are listed in
[Remaining risks and action items](#remaining-risks-and-action-items).

---

## Scope and methodology

### Components inspected

- **Web:** `Program.cs`, all controllers, Razor views, view models, filters,
  authentication/authorization setup, claims factory, CSV helper, image
  validation, CLI maintenance commands, health check.
- **Application:** employee, organization, attendance, leave, lifecycle,
  announcement, document, notification, reporting, dashboard, audit, and user
  administration services and DTOs.
- **Domain:** entities, enums, value objects, domain exceptions.
- **Infrastructure:** EF Core `AppDbContext`, Identity implementation, role/seed
  initialization, file storage, audit service, settings, email sender, clock.
- **Data:** model configuration, constraints, migrations (schema history only;
  no schema changes were made during this audit).
- **Configuration/infrastructure:** `appsettings*.json`, launch settings,
  `Dockerfile`, `docker-compose.yml`, `.dockerignore`, `.gitignore`, CI workflow,
  deployment/development/security docs.
- **Tests:** unit, integration, and E2E suites; existing security tests.

### Security references

- OWASP Top 10 (2021/2025 themes) and OWASP Cheat Sheet Series.
- OWASP ASVS 5.0 themes for authentication, session management, access control,
  input validation, file handling, configuration, and logging.
- NIST SSDF practices for secure configuration, verification, and vulnerability
  response.
- Framework documentation for ASP.NET Core Identity, rate limiting, forwarded
  headers, antiforgery, and EF Core behavior where version-sensitive.

### Tools and commands actually executed

```bash
dotnet --info
dotnet build
dotnet test tests/Emplyo.UnitTests
dotnet test tests/Emplyo.IntegrationTests
dotnet test tests/Emplyo.E2ETests
./scripts/coverage.sh
dotnet list Emplyo.sln package --vulnerable --include-transitive
```

Manual review used repository-wide searches for authorization attributes,
antiforgery usage, raw HTML rendering, SQL construction, file APIs, redirects,
secrets, and cryptographic APIs. `gitleaks`, `semgrep`, `trivy`, and `hadolint`
were **not installed** in the environment; no dedicated SAST, DAST, secret
scanner, or container scanner was run.

### Threat model summary

- **Assets:** credentials and reset tokens, authenticated sessions, employee PII
  (contact data, compensation, photos), uploaded documents, audit records, and
  administrative functions.
- **Trust boundaries:** browser ↔ web application, reverse proxy ↔ application,
  application ↔ SQL Server, application ↔ private document store, application ↔
  SMTP server.
- **Actors:** anonymous Internet attacker, authenticated employee, direct
  manager, HR manager, administrator, and operator with CLI/deployment access.
- **Priority attack paths assessed:** credential guessing/spraying, account
  enumeration, reset-link poisoning, session fixation/revocation, IDOR and
  manager-scope bypass, privileged function access, stored/reflected XSS,
  CSRF, upload/content-type abuse, path traversal, redirect abuse, and unsafe
  production defaults.
- **Controls mapped to paths:** Identity/lockout/rate limiting, security stamps,
  server-side policies plus resource checks, Razor encoding plus CSP, global
  antiforgery, private storage plus authorization, strict URL/redirect handling,
  and documented deployment hardening.

### Secrets review

A manual pattern search of the current tree found no real credentials, API keys,
private keys, or production secrets. Development/demo passwords and the ephemeral
CI SQL Server password are test fixtures and are documented as such. Git history
was not exhaustively scanned, and no secret-scanning tool was available.

### Baseline

The working tree was clean at the start. Baseline build succeeded with zero
warnings; **228 unit** and **80 integration** tests passed; the NuGet vulnerability
audit reported no vulnerable packages. No pre-existing test failures were
observed.

### Limitations and not-assessed areas

- No production environment, cloud account, network, or reverse-proxy
  configuration was available to inspect.
- No external penetration test, fuzzing campaign, load test, or malware analysis
  was performed.
- Container image layers, base-image CVEs, and vendored frontend libraries were
  not scanned with dedicated tools.
- Multi-tenancy was not applicable (single-organization deployment model).
- MFA, external identity providers, payment flows, and webhook integrations are
  not implemented.
- Git history was not exhaustively scanned for historical secrets; only the
  current tree was searched for credential-like patterns.

---

## Findings

Severity reflects exploitability, impact, exposure, and existing mitigations at
the time of discovery. No CVSS 4.0 vector is assigned where the necessary
deployment assumptions could not be verified.

| ID | Title | Severity | Status |
| --- | --- | --- | --- |
| SEC-01 | Password-reset host-header poisoning | High | Fixed and verified |
| SEC-02 | Employee photo IDOR | Medium | Fixed and verified |
| SEC-03 | Account enumeration in reset flow and lockout message | Medium | Fixed and verified |
| SEC-04 | Missing security headers / CSP / framing / referrer controls | Medium | Fixed and verified |
| SEC-05 | No rate limiting on authentication endpoints | Medium | Fixed and verified |
| SEC-06 | Delayed session invalidation after role/link/activation changes | Medium | Fixed and verified |
| SEC-07 | Crafted same-host referer could produce a protocol-relative redirect | Low | Fixed and verified |
| SEC-08 | Client-controlled document MIME type | Low | Fixed and verified |
| SEC-09 | Profile photo accepted any bytes with an image extension | Low | Fixed and verified |
| SEC-10 | No application-level request/form-size limits | Low | Fixed |
| SEC-11 | Development seed command usable outside Development | Medium | Fixed and verified |
| SEC-12 | Last active administrator could be removed or deactivated | Low | Fixed and verified |
| SEC-13 | Unbounded inputs on several authentication/settings/file forms | Low | Fixed |
| SEC-14 | Inactive account check relied solely on lockout | Low | Fixed |
| SEC-15 | Forwarded headers not configurable behind a reverse proxy | Low | Fixed (configuration support + docs) |
| SEC-16 | Storage extension not sanitized for path separators | Informational | Fixed |
| SEC-17 | Password policy allowed low character diversity | Informational | Fixed |
| SEC-18 | Antiforgery cookie attributes were implicit | Informational | Fixed |

### SEC-01 — Password-reset host-header poisoning

- **Severity:** High (conditional on email delivery being enabled).
- **Affected:** `src/Emplyo.Web/Controllers/AccountController.cs`
  (`ForgotPassword`), `src/Emplyo.Web/appsettings.json`.
- **Weakness:** The reset link was generated with `Url.Action(..., Request.Scheme)`
  and therefore used the request `Host`. `AllowedHosts` was `*`. An attacker able
  to submit a forgot-password request with a forged `Host` could cause the victim
  to receive a reset link for an attacker-controlled origin (CWE-640).
- **Evidence:** Code review showed the link was built from the request, and the
  default host allow list was unrestricted. The new regression test sends
  `Host: evil.example` after configuring `App:PublicBaseUrl` and asserts that the
  captured email contains only the configured origin.
- **Impact:** Account takeover if the victim follows the poisoned link and
  submits a new password on an attacker-controlled site.
- **Root cause:** Trusting request-derived authority data for a security-sensitive
  absolute URL.
- **Remediation:** Reset links are built from `App:PublicBaseUrl`; outside
  Development the URL must be absolute HTTPS, contain no user-info/query/fragment,
  and is refused otherwise. The link is HTML-encoded. A startup warning is emitted
  when email is enabled without a public base URL. The default `AllowedHosts` is
  now `localhost;127.0.0.1`, and non-Development startup warns when it is
  wildcarded.
- **Verification:** `Forgot_password_uses_the_configured_public_base_url_not_the_request_host`
  and `Requests_with_an_untrusted_host_header_are_rejected` integration tests
  pass.
- **Residual risk:** If email is enabled and `App:PublicBaseUrl` is wrong, reset
  links are wrong or suppressed; this is an operational configuration issue.
- **References:** OWASP A01/A07 theme, CWE-640, ASVS authentication/host-header
  requirements.

### SEC-02 — Employee photo IDOR

- **Severity:** Medium.
- **Affected:** `src/Emplyo.Web/Controllers/EmployeesController.cs` (`Photo`).
- **Weakness:** The photo action required authentication but performed no
  resource-level authorization. Any authenticated employee could enumerate
  `/Employees/Photo/{id}` and retrieve photos of any employee, including
  employees whose details they could not view (CWE-639).
- **Evidence:** The action only loaded the employee and checked that a stored
  file existed. The new integration tests show an unrelated employee is denied
  while the owner, direct manager, and staff are allowed.
- **Impact:** Unauthorized disclosure of employee photos/PII.
- **Root cause:** An alternate read path for employee data did not reuse the
  details-page authorization rule.
- **Remediation:** The same staff/self/direct-manager rule is now enforced for
  photos, and file content type is derived from the validated extension.
- **Verification:** `An_employee_cannot_read_another_employees_photo` and
  `A_manager_can_read_a_direct_reports_photo_but_not_an_unrelated_photo` pass.
- **Residual risk:** Photos are still visible to direct managers by design.
- **References:** OWASP A01, CWE-639, ASVS access-control requirements.

### SEC-03 — Account enumeration

- **Severity:** Medium.
- **Affected:** `AccountController.Login`, `AccountController.ResetPassword`.
- **Weakness:** Two observable differences revealed account existence:
  1. A reset POST with a bogus token redirected to success for a non-existent
     account but rendered an “Invalid token” error for an existing account.
  2. The login response explicitly said an account was locked after repeated
     failures, while unknown accounts kept the generic message.
- **Impact:** Attackers could confirm valid corporate emails, enabling targeted
  phishing or password spraying.
- **Root cause:** Different error branches for existing and non-existing
  identities.
- **Remediation:** Invalid/expired/unknown reset submissions now return the same
  generic message. Locked-out, invalid-password, and unknown-account sign-ins
  return the same generic message; lockout is still logged and enforced.
- **Verification:** `Reset_password_does_not_reveal_whether_an_account_exists`
  passes; the lockout integration test asserts the generic response and verifies
  lockout through `UserManager`.
- **Residual risk:** Timing differences are still theoretically observable;
  test coverage addresses response-content enumeration.
- **References:** OWASP A07, CWE-204/CWE-307, ASVS authentication requirements.

### SEC-04 — Missing security headers

- **Severity:** Medium.
- **Affected:** Request pipeline (`Program.cs`), new
  `src/Emplyo.Web/Security/SecurityHeadersMiddleware.cs`.
- **Weakness:** No CSP, `X-Content-Type-Options`, `X-Frame-Options`, referrer
  policy, permissions policy, or explicit cache policy was set. This left
  clickjacking, MIME-sniffing, and referrer leakage as defense-in-depth gaps.
- **Impact:** Increased impact of any future XSS/content-injection defect;
  possible clickjacking of authenticated actions; possible reset-token leakage in
  `Referer` and caches.
- **Root cause:** No centralized response-hardening middleware.
- **Remediation:** Added self-only CSP (`script-src 'self'`, no framing/objects),
  `nosniff`, `DENY`, permissions policy, COOP/CORP, `Cache-Control: no-store` on
  dynamic responses, and `no-referrer` on account/download paths. HSTS is one
  year with subdomains outside Development.
- **Verification:** Security-header integration test passes; all E2E browser tests
  pass with the CSP active.
- **Residual risk:** `style-src` allows inline styles because the design system
  uses inline width styles; no `report-uri`/reporting endpoint exists.
- **References:** OWASP A05, CWE-693/CWE-1021, ASVS configuration requirements.

### SEC-05 — No authentication rate limiting

- **Severity:** Medium.
- **Affected:** `AccountController` login/forgot/reset POST actions,
  `Program.cs`.
- **Weakness:** Account lockout limited per-account password guessing, but there
  was no per-client limit for password spraying, reset-email flooding, or token
  guessing.
- **Impact:** Distributed/sprayed credential attacks and email/notification abuse.
- **Root cause:** No rate-limiting middleware or policy.
- **Remediation:** ASP.NET Core fixed-window rate limiting on the three anonymous
  POST endpoints, partitioned by remote IP, default 30/minute, configurable via
  `Security:AuthRateLimitPerMinute`. Trusted-proxy configuration is supported so
  forwarded client IPs can be used safely.
- **Verification:** `Repeated_login_posts_are_rate_limited` returns two normal
  responses and then HTTP 429 with a test limit of two.
- **Residual risk:** Behind an unconfigured proxy, users may share a partition;
  rate limiting at the edge is recommended. A determined attacker with many IPs
  is not fully mitigated.
- **References:** OWASP A07, CWE-307, ASVS authentication requirements.

### SEC-06 — Delayed session invalidation

- **Severity:** Medium.
- **Affected:** `IdentityService`, `UserAdminService`, `Program.cs`.
- **Weakness:** Role, activation, and employee-link changes did not update the
  Identity security stamp, and the default revalidation interval was 30 minutes.
  A demoted/deactivated user could retain stale role or employee claims for up to
  30 minutes.
- **Impact:** Continued access after privilege revocation, including access to a
  previously linked employee's self-service pages.
- **Root cause:** No security-stamp invalidation and a long default validation
  interval.
- **Remediation:** Security stamps are updated on role changes, activation
  changes, and user updates (which include employee links). The interval is
  configurable and defaults to 5 minutes (`Security:SecurityStampValidationMinutes`).
- **Verification:** `Role_demotion_invalidates_an_existing_session` (validation
  interval zero) shows the old admin cookie can no longer access `/Users`.
- **Residual risk:** Up to 5 minutes of residual access by default; immediate
  revocation would require a session store or shorter interval with more DB load.
- **References:** OWASP A01/A07, CWE-613, ASVS session-management requirements.

### SEC-07 — Crafted referer redirect edge case

- **Severity:** Low.
- **Affected:** `src/Emplyo.Web/Filters/DomainExceptionFilter.cs`.
- **Weakness:** The safe-referer helper rejected external authorities and
  protocol-relative relative values, but a same-host absolute URL with a
  backslash path could be normalized into a `//host`-like redirect target.
- **Impact:** Potential open redirect under attacker-influenced `Referer` values.
- **Root cause:** Path validation focused on the original string rather than the
  parsed `PathAndQuery`.
- **Remediation:** The parsed same-origin path must start with a single `/`, must
  not start with `//` or `/\`, and must not contain any backslash.
- **Verification:** `A_domain_error_does_not_redirect_to_a_backslash_referer`
  asserts a `/` redirect.
- **Residual risk:** Browsers control `Referer`, limiting practical exploitation.
- **References:** OWASP A01, CWE-601.

### SEC-08 — Client-controlled document MIME type

- **Severity:** Low.
- **Affected:** `src/Emplyo.Application/Documents/DocumentService.cs`.
- **Weakness:** The stored `ContentType` came from the client after an allow-list
  check. A client could label a file with a different allowed type than its
  extension, influencing how the file is later served.
- **Impact:** Content-type confusion; potential browser-side parsing issues.
- **Root cause:** Trusting client metadata instead of deriving the type.
- **Remediation:** The stored content type is derived from the validated
  extension; client MIME types are still checked but never stored.
- **Verification:** Unit test `Upload_derives_the_content_type_from_the_extension`
  passes. Downloads use `Content-Disposition: attachment` and `nosniff` is set.
- **Residual risk:** Uploaded documents are not malware-scanned.
- **References:** OWASP A04/A08 themes, CWE-436.

### SEC-09 — Weak profile-photo validation

- **Severity:** Low.
- **Affected:** `EmployeesController.UploadPhoto`,
  `ProfileController.UploadPhoto`, new `Services/ImageUploads.cs`.
- **Weakness:** Only filename extension and declared length were checked. Any
  bytes could be stored with an image extension and served as an image.
- **Impact:** Storage of unexpected content; increased impact if response headers
  were ever misconfigured.
- **Root cause:** No content-signature validation.
- **Remediation:** JPEG/PNG/WebP signatures are checked and must match the
  extension; files over 4 MB or with disallowed extensions are rejected.
- **Verification:** Eight `ImageUploadsTests` cover accepted formats, HTML
  disguised as an image, extension/signature mismatch, disallowed extension,
  oversize, and empty input. E2E tests still pass.
- **Residual risk:** Image decoders are not invoked, so malformed-but-signed
  images can still be stored; no malware scan is performed.
- **References:** OWASP file-upload guidance, CWE-434.

### SEC-10 — Missing request/form-size limits

- **Severity:** Low.
- **Affected:** `Program.cs`; upload services.
- **Weakness:** The application relied on default ASP.NET Core limits and
  post-read size checks, leaving room for unnecessary large-body buffering.
- **Impact:** Resource-exhaustion risk for authenticated uploads and anonymous
  authentication posts.
- **Root cause:** No application-specific transport/form limits.
- **Remediation:** Kestrel maximum request body 16 MB, multipart body 12 MB,
  individual form value 1 MB, form value count 1024. Business limits remain
  10 MB for documents and 4 MB for photos.
- **Verification:** Build/tests pass; limits are configuration-level and were
  verified by review rather than by an oversized-request integration test.
- **Residual risk:** No load test or per-user quota.
- **References:** OWASP ASVS input/resource-consumption guidance, CWE-400.

### SEC-11 — Development seed usable outside Development

- **Severity:** Medium (operator-error scenario).
- **Affected:** `src/Emplyo.Web/Services/CliCommands.cs`.
- **Weakness:** `seed-dev-data` could run against a Production database and create
  known demo accounts with an operator-supplied password.
- **Impact:** Well-known accounts/roles in a production dataset.
- **Root cause:** No environment guard.
- **Remediation:** The command refuses unless the environment is Development or
  `EMPLYO_ALLOW_DEV_SEED=true` is explicitly set.
- **Verification:** `Seed_dev_data_is_refused_in_production` passes.
- **Residual risk:** An operator can still set the override deliberately.
- **References:** OWASP A05, CWE-1188.

### SEC-12 — Last administrator removable

- **Severity:** Low.
- **Affected:** `src/Emplyo.Application/Users/UserAdminService.cs`.
- **Weakness:** An administrator could demote or deactivate the last active
  administrator, removing UI access to administration.
- **Impact:** Availability/administrative lockout.
- **Root cause:** No invariant enforcing at least one active administrator.
- **Remediation:** `UpdateAsync` refuses changes that would leave no other active
  Administrator.
- **Verification:** Two unit tests cover refusal and the allowed case with a
  second admin.
- **Residual risk:** Concurrent demotions could race; recovery remains possible
  through the documented `seed-admin` CLI when no admin exists.
- **References:** OWASP A01 business-logic theme, CWE-269.

### SEC-13 — Unbounded form inputs

- **Severity:** Low.
- **Affected:** `AccountController` view models, `SettingsController`, contact
  fields, `DocumentService`.
- **Weakness:** Several inputs lacked maximum lengths matching database columns,
  allowing avoidable large payloads and unhandled database errors.
- **Impact:** Low-severity resource consumption and error handling noise.
- **Root cause:** Incomplete DTO annotations.
- **Remediation:** Added email/password/token, settings, contact, document title,
  and original-filename limits, plus server-side document title checks.
- **Verification:** Build/unit/integration tests pass.
- **Residual risk:** Not exhaustively fuzzed.
- **References:** OWASP ASVS validation guidance, CWE-20.

### SEC-14 — Inactive account check relied on lockout

- **Severity:** Low.
- **Affected:** `AccountController.Login`.
- **Weakness:** The explicit `IsActive` check happened only indirectly through
  lockout state. If an account were marked inactive without lockout in the
  future, sign-in could succeed.
- **Impact:** Defense-in-depth gap.
- **Root cause:** Business activation state was not checked at sign-in.
- **Remediation:** After a successful password check, an inactive or missing user
  is signed out and receives the generic failure response.
- **Verification:** Covered by the lockout test’s verified lockout state and
  successful-login tests.
- **Residual risk:** None material; the lockout control remains primary.
- **References:** OWASP A07, CWE-287.

### SEC-15 — Forwarded-header configuration

- **Severity:** Low.
- **Affected:** `Program.cs`, deployment documentation.
- **Weakness:** The application did not consume proxy `X-Forwarded-For/Proto`
  headers, which can cause HTTPS redirect loops and ineffective per-IP rate
  limiting behind a TLS-terminating proxy.
- **Impact:** Availability/configuration risk and weaker rate limiting in
  proxied deployments.
- **Root cause:** No forwarded-header middleware or trusted-proxy configuration.
- **Remediation:** Forwarded headers are processed only from loopback/configured
  `Security:KnownProxies`; deployment documentation explains configuration and
  header overwriting. The safe default is to ignore untrusted headers.
- **Verification:** Configuration reviewed; E2E/integration tests run with the
  middleware enabled. No real proxy was available for end-to-end validation.
- **Residual risk:** Actual proxy topology must be verified during deployment.
- **References:** ASP.NET Core proxy guidance, CWE-346.

### SEC-16 — Storage extension sanitization

- **Severity:** Informational.
- **Affected:** `src/Emplyo.Infrastructure/Services/FileStorageService.cs`.
- **Weakness:** The generated stored filename appended the original extension
  after limiting its length; unusual separator characters could influence path
  construction.
- **Impact:** Defense-in-depth only; callers already validate extensions and the
  storage path is confined.
- **Remediation:** Extensions containing non-alphanumeric/non-dot characters are
  discarded.
- **Verification:** Existing path-traversal unit tests pass.
- **References:** CWE-22.

### SEC-17 — Password character diversity

- **Severity:** Informational.
- **Affected:** `Program.cs` Identity options.
- **Weakness:** The default minimum unique-character count was 1 despite a
  12-character policy.
- **Remediation:** Required unique characters raised to 4.
- **Verification:** All test credentials and flows still pass.
- **References:** OWASP authentication guidance.

### SEC-18 — Antiforgery cookie attributes

- **Severity:** Informational.
- **Affected:** `Program.cs`.
- **Weakness:** Cookie security depended on framework defaults and was not
  explicit.
- **Remediation:** Antiforgery cookie name and `HttpOnly`/`SameSite=Lax`/`Secure`
  policy are now explicit and match the auth cookie.
- **Verification:** CSRF integration test remains green.

---

## Changes implemented

### Application/security logic

| File | Change |
| --- | --- |
| `src/Emplyo.Web/Controllers/AccountController.cs` | Configured reset-link base URL; HTML-encoded link; neutral reset and lockout responses; inactive-account sign-out; input limits; auth rate-limit attributes |
| `src/Emplyo.Web/Controllers/EmployeesController.cs` | Photo resource authorization; shared staff/self/manager rule; image validation |
| `src/Emplyo.Web/Controllers/ProfileController.cs` | Shared signature-validated photo upload; contact-field limits |
| `src/Emplyo.Web/Controllers/SettingsController.cs` | Bound settings inputs |
| `src/Emplyo.Web/Controllers/HomeController.cs` | Role constants instead of string literals |
| `src/Emplyo.Web/Filters/DomainExceptionFilter.cs` | Strict same-origin referer path validation |
| `src/Emplyo.Web/Services/ImageUploads.cs` | New JPEG/PNG/WebP signature/extension/size validator |
| `src/Emplyo.Web/Security/SecurityHeadersMiddleware.cs` | New security-header/cache/referrer middleware |
| `src/Emplyo.Web/Services/CliCommands.cs` | Refuse dev seeding outside Development |
| `src/Emplyo.Web/Program.cs` | Rate limiter, security-stamp interval, forwarded headers, body limits, HSTS/antiforgery options, password diversity, startup warning |
| `src/Emplyo.Web/appsettings.json` | New `App`/`Security` settings |
| `src/Emplyo.Application/Users/UserAdminService.cs` | Last-admin invariant; security-stamp refresh |
| `src/Emplyo.Application/Attendance/AttendanceService.cs` | Reject attendance record/employee mismatch |
| `src/Emplyo.Application/Documents/DocumentService.cs` | Server-derived content type; title/name limits |
| `src/Emplyo.Application/Common/Interfaces/IIdentityService.cs` | Security-stamp refresh contract |
| `src/Emplyo.Infrastructure/Services/IdentityService.cs` | Security-stamp updates on role/activation changes |
| `src/Emplyo.Infrastructure/Services/FileStorageService.cs` | Extension sanitization |
| `docker-compose.yml` | Drop all Linux capabilities; public URL/host configuration passthrough |
| `.github/workflows/ci.yml` | Disable persisted checkout credentials |

No dependencies were added, removed, or upgraded. No database schema change was
made.

### Tests added/updated

- `tests/Emplyo.IntegrationTests/SecurityTests.cs` — security headers, photo
  authorization, auth rate limiting, session invalidation, backslash referer,
  enumeration-resistant lockout.
- `tests/Emplyo.IntegrationTests/PasswordResetTests.cs` — reset enumeration
  resistance and public-base-URL host-header handling.
- `tests/Emplyo.IntegrationTests/CliCommandTests.cs` — production seed refusal.
- `tests/Emplyo.IntegrationTests/EmplyoWebAppFactory.cs` — configurable test
  limits/intervals.
- `tests/Emplyo.UnitTests/ImageUploadsTests.cs` — image signature validation.
- `tests/Emplyo.UnitTests/UserAdminServiceTests.cs` — last-admin invariant and
  security-stamp interface fake.
- `tests/Emplyo.UnitTests/AttendanceServiceTests.cs` — attendance-record ownership.
- `tests/Emplyo.UnitTests/DocumentServiceTests.cs` — derived content type.

### Documentation

- `docs/SECURITY.md` — updated controls and limitations.
- `docs/DEPLOYMENT.md` — public base URL, host allow list, trusted proxies,
  TLS/database guidance, seed guard, capability drop.
- `README.md`, `docs/DEVELOPMENT.md`, `docs/PROJECT-STRUCTURE.md`,
  `docs/ARCHITECTURE.md`, `docs/README.md` — configuration, counts, structure,
  pipeline, and audit index updates.
- `docs/security/SECURITY_AUDIT.md` — this report.

---

## Verification results

### Passed

| Check | Result |
| --- | --- |
| `dotnet build` (Debug) | 0 warnings, 0 errors |
| `dotnet build -c Release` | 0 warnings, 0 errors |
| Release unit/integration/E2E runs | 239 / 90 / 7 passed, 0 failed |
| Unit tests | 239 passed, 0 failed |
| Integration tests (SQL Server) | 90 passed, 0 failed |
| E2E tests (Chromium, CSP active) | 7 passed, 0 failed |
| `./scripts/coverage.sh` | 59.59% lines / 68.50% branches — gate passed (57%/63%) |
| NuGet vulnerable-package audit | No vulnerable packages reported across all 7 projects |
| CSRF rejection regression test | Passed |
| Auth rate-limit test | Passed (HTTP 429 after configured limit) |
| Password-reset host-header test | Passed |
| Untrusted Host header rejection test | Passed (HTTP 400) |
| Reset account-enumeration test | Passed |
| Photo IDOR tests | Passed (unrelated denied; owner/manager/staff allowed) |
| Session invalidation test | Passed |
| Production seed-refusal test | Passed |
| Image validation tests | Passed |
| Last-admin invariant tests | Passed |

### Failed

None in the final run.

### Skipped

- External penetration test, DAST, fuzzing, load testing, container-image
  scanning, SBOM generation, and centralized monitoring were not performed
  because no such tooling/environment was available in this session.
- Deployment-time TLS, reverse-proxy, SMTP, and SQL Server certificate behavior
  were not validated against a real production topology.

### Inconclusive

- Practical exploitability of the pre-fix host-header issue depends on the
  production proxy forwarding an arbitrary `Host` and email being enabled; the
  code-level weakness and the fix are confirmed.
- Timing-based account enumeration was not measured.

---

## Remaining risks and action items

### Fixed and verified

- Password-reset host-header poisoning.
- Employee photo IDOR.
- Reset/lockout account enumeration.
- Missing security headers/CSP/framing/referrer/cache controls.
- Missing authentication rate limiting.
- Delayed session invalidation.
- Crafted-referer redirect edge case.
- Client-controlled document MIME type.
- Weak photo upload validation.
- Production-unsafe demo seeding.
- Last-admin removal.
- Attendance-record ownership mismatch.

### Fixed but not fully verified

- Request-size limits are code-reviewed and exercised indirectly but not tested
  with deliberately oversized requests.
- Trusted-proxy forwarded-header behavior is configuration-supported but not
  validated against a real reverse proxy.
- HSTS/subdomain behavior and Secure-cookie behavior were not tested over real
  HTTPS.
- The fail-closed branch that suppresses reset email when `App:PublicBaseUrl` is
  missing/insecure outside Development was code-reviewed but not dynamically
  exercised.

### Mitigated but not fully resolved

- Session revocation now has a bounded window (default 5 minutes), not immediate
  revocation.
- Rate limiting is per directly observed IP and requires correct proxy
  configuration; edge rate limiting is still recommended.
- CSP restricts scripts to same origin but permits inline styles.

### Identified but not fixed (requires environment/operator action)

1. **Enable MFA** for administrators and HR users (not implemented).
2. **Add malware scanning / CDR** for documents before making them available to
   employees.
3. **Production host allow list**: the safe default is `localhost;127.0.0.1`;
   set `AllowedHosts` to the real production host names.
4. **TLS everywhere**: terminate TLS at a trusted proxy/host, set
   `App__PublicBaseUrl` to the HTTPS origin, and verify HSTS behavior.
5. **Database least privilege and transport security**: do not use `sa`; use a
   dedicated login and a trusted SQL Server certificate
   (`TrustServerCertificate=False` in production).
6. **Proxy trust**: configure `Security__KnownProxies` for the actual proxy and
   overwrite forwarding headers at the edge.
7. **Monitoring/alerting**: ship structured logs and audit events to a monitored,
   tamper-resistant store; alert on lockouts, 429s, role changes, and document
   downloads.
8. **Tooling**: add SAST, secret scanning, container-image scanning, and SBOM
   generation to CI; currently only NuGet auditing is automated.
9. **Backups and key management**: protect the document store, database backups,
   and Data Protection key ring; test restore procedures.
10. **Frontend dependency review**: vendored Bootstrap/jQuery/Chart.js are not
    covered by an automated scanner.
11. **Reset-token log hygiene**: reset tokens travel in one-time link query
    strings. Configure proxy/access-log redaction and retention so they are not
    retained in logs.
12. **Health endpoint exposure**: `GET /health` is anonymous and reveals only
    liveness/database connectivity. Restrict it at the network/proxy layer if the
    application is Internet-facing.

### Not assessed

- Payment/subscription logic (not present).
- Multi-tenant isolation (not present).
- OAuth/OIDC/JWT (not present).
- Webhooks (not present).
- Cloud IAM, network segmentation, WAF, DDoS protection.
- Production data retention/deletion procedures.

---

## Security verification checklist

| Area | Status | Evidence / note |
| --- | --- | --- |
| Authentication | Verified | Identity password hashing, 12-char policy, 4 unique chars, lockout, neutral responses, reset flow tests |
| MFA | Not verified (not implemented) | Listed as a residual risk and remediation item |
| Session management | Partially verified | HttpOnly/Secure/SameSite cookies, 8-hour sliding expiry, security-stamp revocation test; no absolute idle timeout review beyond config |
| Authorization | Verified | Policy tests, resource-based leave/document/photo/attendance tests, manager-scope tests |
| IDOR / object-level authorization | Verified | Photo and document regression tests; attendance/service ownership guard |
| CSRF | Verified | Global antiforgery; missing-token test returns 400 |
| SQL injection | Verified by review | EF Core parameterized queries; no raw SQL in production code |
| XSS / output encoding | Verified by review + CSP | Razor encoding; `Html.Raw` limited to encoding badge helper; CSP active; no DAST |
| Open redirect | Verified | Local-URL check plus strict referer path test |
| Host-header handling | Verified | Reset-link test with malicious Host; docs for `AllowedHosts` |
| Security headers / CSP | Verified | Integration header assertions; E2E under CSP |
| Cache / referrer leakage | Verified | `no-store` and account-path `no-referrer` assertions |
| Rate limiting | Verified | 429 regression test |
| Password reset | Verified | Token flow, enumeration resistance, host-header handling |
| File upload security | Partially verified | Extension/size/signature/content-type controls and tests; no malware scanner |
| File download authorization | Verified | Owner/staff/confidentiality tests |
| Path traversal | Verified | Storage unit tests |
| Secrets in source | Partially verified | Manual pattern search; no dedicated secret scanner or full git-history scan |
| Dependency vulnerabilities | Verified | NuGet audit, no findings |
| Frontend/vendored dependencies | Not verified | No SCA coverage |
| Container/infrastructure | Partially verified | Non-root image, capability drop, no-new-privileges reviewed; no image scan or deployment test |
| Audit logging | Partially verified | Sensitive operations audited; no external SIEM/WORM verification |
| Error handling | Verified | Friendly error pages; no stack traces outside Development |
| Business logic | Verified for tested flows | Self-approval, balance, lifecycle, last-admin, seed guard tests |
| Multi-tenancy | Not applicable | Single-organization model |
| Privacy/PII | Partially verified | Compensation restricted, photos/documents authorized; retention policy is organizational |
| SAST/DAST | Not performed | No tools available/configured |
| Penetration test | Not performed | Out of scope for this session |
