# SECURITY_ASSESSMENT

## Scope

This assessment documents the current security implementation status of the `AccountingSystem` solution (`AccountingSystem.Api` and `AccountingSystem.Client`) for IT16 Information Security 1 criteria.

This document is descriptive only and does not alter code, schema, migrations, or runtime configuration.

## Implemented Security Feature Status

| Capability | Current Status | Evidence in Repository |
| --- | --- | --- |
| Login | Implemented | `AccountingSystem.Api/Controllers/AuthController.cs` (`POST /api/auth/login`) |
| JWT issuance and validation | Implemented | `AccountingSystem.Api/Services/JwtAuthTokenFactory.cs`, `AccountingSystem.Api/Program.cs` |
| Forgot password | Implemented | `POST /api/auth/forgot-password`, `AuthService.SendPasswordResetAsync` |
| Reset password | Implemented | `POST /api/auth/reset-password`, `AuthService.ResetPasswordAsync` |
| Email confirmation | Implemented | `POST /api/auth/confirm-email`, `AuthService.ConfirmEmailAsync` |
| Resend confirmation | Implemented | `POST /api/auth/resend-confirmation`, `AuthService.ResendConfirmationAsync` |
| MFA (Authenticator App, Email OTP, recovery codes) | Implemented | `/api/auth/login/mfa`, `/api/auth/login/mfa/email/send`, and `/api/auth/mfa/*` endpoints |
| Registration bot protection | Implemented | reCAPTCHA token from `RegisterCompany.razor` validated by `CaptchaService` |
| Always-on login bot protection | Implemented | login reCAPTCHA is shown by default and required server-side for every non-locked login attempt; public site key is client-side |
| Backup SuperAdmin support | Implemented | `SuperAdminController` supports SuperAdmin account listing/creation/status changes with last-active protection (last active SuperAdmin cannot be disabled or deleted) |
| PayMongo source + redirect payment flow | Implemented (test mode for project use) | `PaymentController.CreateSource`, client payment callback page |
| PayMongo webhook verification hardening | Implemented | `PaymentService.VerifyWebhookSignature` validates HMAC signature and replay window |

## Password Storage and Policy

### Password Policy

Password policy is enforced through `AccountingSystem.Shared/Validation/PasswordPolicy.cs`:

- Complex password path: minimum 12 characters and at least 3 of 4 character classes.
- Passphrase path: minimum 16 characters with at least 3 words.
- Maximum length: 128 characters.

### Hashing Strategy in Current Implementation

- Identity-provisioned accounts use ASP.NET Core Identity password hashing (`UserManager.PasswordHasher`).
- Legacy password fields (`PasswordHash`, `PasswordSalt`) remain for compatibility and fallback validation through `LegacyPasswordService`.
- New registration/admin-created users are provisioned to Identity (`EnsureProvisionedAsync`), while legacy compatibility remains active.

## Multi-Factor Authentication

- Authenticator App MFA and Email OTP MFA are optional and independently managed from the user profile.
- Email OTP MFA requires a confirmed email address and does not require Authenticator App MFA to be enabled.
- Users, including SuperAdmins, can resend email confirmation before enabling Email OTP MFA.
- Recovery codes remain available for Authenticator App MFA where valid recovery codes exist.
- Sensitive SuperAdmin governance actions (create/enable/disable SuperAdmin accounts) require step-up verification with password re-entry and MFA when enabled.

## Lockout and Rate Limiting

Default security controls from configuration and startup:

- Lockout threshold: 5 failed attempts.
- Lockout duration: 5 minutes for demo/presentation.
- The login UI intentionally avoids showing exact attempts left or a countdown. It uses generic messages to reduce attacker feedback.
- Login reCAPTCHA is always required before credential processing; account lockout still applies after the configured failed attempts.
- Endpoint-specific rate limits exist for login, register-company, forgot/reset password, confirm/resend confirmation, MFA login, and MFA management.

## Secret and Configuration Handling

- API startup loads `.env` during local development (`DotNetEnv.Env.Load()`).
- Sensitive settings in `AccountingSystem.Api/appsettings.json` are placeholder-based (`__SET_VIA_ENV__`), not live credentials.
- Expected secret sources: environment variables and local `.env` (developer machine).
- SMTP, PayMongo secret key, and reCAPTCHA secret are required outside Development.
- reCAPTCHA uses a client-side public site key in Blazor auth pages, while `Recaptcha:SecretKey` remains server-only through environment/configuration.
- The seeded/demo bootstrap SuperAdmin is email-confirmed for MFA demonstration; backup SuperAdmins created in the UI use the standard email confirmation flow.

## Authorization and Tenant Isolation

- Roles used across API and client: `Admin`, `Accounting`, `Management`, `SuperAdmin`.
- API access control is enforced with `[Authorize]` and `[Authorize(Roles = ...)]`.
- Client route/UI checks use `AuthorizeRouteView` and role-based views.
- Last-active SuperAdmin protection prevents disabling or deleting the final active SuperAdmin account.
- Tenant isolation is enforced by:
  - `TenantAccessMiddleware` user/company status checks
  - EF Core query filters scoped by `CompanyId`.

## Logging and Monitoring

- `AuditMiddleware` logs successful state-changing non-auth routes.
- Auth security events are logged by `AuthSecurityAuditService` (login outcomes, lockouts, auth rate-limit events, MFA events, etc.).
- Tenant audit logs show System and Security categories.
- Super-admin governance actions are logged in `SuperAdminAuditLogs`.
- Failed login, lockout, CAPTCHA-required, MFA-challenge, and login-success events targeting SuperAdmin accounts are mirrored into SuperAdmin governance logs.
- Backup SuperAdmin creation, enable/disable actions, and last-active-SuperAdmin protection are logged as SuperAdmin governance events.
- Step-up verification outcomes for sensitive SuperAdmin governance actions are logged (`SUPERADMIN-STEPUP-SUCCESS` / `SUPERADMIN-STEPUP-FAILED`) with safe metadata and required reason/justification.
- SuperAdmin governance logs do not include current password, MFA codes, Email OTP values, recovery codes, CAPTCHA tokens, JWTs, or secrets.
- Passwords, OTP values, recovery codes, CAPTCHA tokens, JWTs, and secrets are not written to audit details.

## Security Risks by Severity

### High

1. **Known Limitation:** JWT token is stored in browser local storage.
   - Risk: token exposure risk increases if XSS is introduced.
2. **Known Limitation:** Email OTP challenge storage is in-memory for demo use.
   - Risk: pending OTP challenges are lost if the API restarts or scales across multiple instances without shared state.

### Medium

1. **Recommended Improvement:** Add refresh-token and revocation controls.
   - Current model relies on JWT expiry without dedicated revocation store.
2. **Recommended Improvement:** Reduce duplicate JWT validation paths.
   - Both JWT bearer auth and custom middleware validation are active.

### Process and Assurance Gaps

1. **Recommended Improvement:** Strengthen CI enforcement thresholds.
   - Repository now includes active workflow evidence in `.github/workflows` for build/test checks, dependency vulnerability reporting, secrets scanning evidence, and CodeQL analysis.
   - Current posture is intentionally evidence-first/non-blocking for dependency and secret scanning while remediation baseline is established.

## Code Auditing and Tooling

### Current Evidence

- Unit tests exist for authentication and account flows (`AccountingSystem.API.Tests`).
- Runtime audit records exist for business mutations and auth-security events.
- CI workflow `.github/workflows/security-tooling-evidence.yml` provides:
  - build/test evidence
  - dependency vulnerability report artifacts
  - gitleaks secret scan evidence (report-first/non-blocking)
- CI workflow `.github/workflows/codeql.yml` provides CodeQL static analysis for C#.

### Recommended Toolchain Additions

- Tighten fail-gates after baseline remediation (for example, severity thresholds for vulnerabilities and secret findings).
- Expand static analysis/style checks as policy gates once warning baseline is normalized.
- Optional DAST baseline for public endpoints in staging.

## Evidence Checklist

- [x] Login endpoint and JWT issuance
- [x] Forgot password email workflow
- [x] Reset password workflow
- [x] Email confirmation flow
- [x] Resend confirmation flow
- [x] MFA login and MFA management endpoints
- [x] reCAPTCHA-protected registration
- [x] Always-on login reCAPTCHA
- [x] Client-side public reCAPTCHA site key and server-side secret key handling
- [x] Backup SuperAdmin support and last-active protection
- [x] PayMongo source/redirect test flow
- [x] Protected dashboard and role-based pages
- [x] Audit logs and auth security audit events

## Conclusion

The project has a substantial implemented authentication and authorization foundation for IT16, including account recovery, email confirmation, optional independently managed Authenticator App MFA, optional Email OTP MFA, recovery codes, lockout, always-on login reCAPTCHA, rate limiting, SuperAdmin governance step-up verification for sensitive SuperAdmin actions, audit logging, PayMongo webhook signature validation, and CI-backed security-tooling evidence. If a SuperAdmin account is compromised, operational response should use a trusted backup SuperAdmin to review governance logs, disable suspicious accounts, reset credentials, and rotate affected secrets as needed. The most important remaining hardening items are stronger token lifecycle controls, production-grade/shared Email OTP challenge storage, and tightening CI security enforcement thresholds after baseline remediation.
