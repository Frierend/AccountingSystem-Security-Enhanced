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
| Adaptive login bot protection | Implemented | login reCAPTCHA is required only after repeated failed attempts |
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

## Lockout and Rate Limiting

Default security controls from configuration and startup:

- Lockout threshold: 5 failed attempts.
- Lockout duration: 5 minutes for demo/presentation.
- The login UI intentionally avoids showing exact attempts left or a countdown. It uses generic messages to reduce attacker feedback.
- Endpoint-specific rate limits exist for login, register-company, forgot/reset password, confirm/resend confirmation, MFA login, and MFA management.

## Secret and Configuration Handling

- API startup loads `.env` during local development (`DotNetEnv.Env.Load()`).
- Sensitive settings in `AccountingSystem.Api/appsettings.json` are placeholder-based (`__SET_VIA_ENV__`), not live credentials.
- Expected secret sources: environment variables and local `.env` (developer machine).
- SMTP, PayMongo secret key, and reCAPTCHA secret are required outside Development.

## Authorization and Tenant Isolation

- Roles used across API and client: `Admin`, `Accounting`, `Management`, `SuperAdmin`.
- API access control is enforced with `[Authorize]` and `[Authorize(Roles = ...)]`.
- Client route/UI checks use `AuthorizeRouteView` and role-based views.
- Tenant isolation is enforced by:
  - `TenantAccessMiddleware` user/company status checks
  - EF Core query filters scoped by `CompanyId`.

## Logging and Monitoring

- `AuditMiddleware` logs successful state-changing non-auth routes.
- Auth security events are logged by `AuthSecurityAuditService` (login outcomes, lockouts, auth rate-limit events, MFA events, etc.).
- Super-admin governance actions are logged in `SuperAdminAuditLogs`.
- Failed login, lockout, CAPTCHA-required, MFA-challenge, and login-success events targeting SuperAdmin accounts are mirrored into SuperAdmin governance logs.

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
- [x] Adaptive login reCAPTCHA after repeated failed attempts
- [x] PayMongo source/redirect test flow
- [x] Protected dashboard and role-based pages
- [x] Audit logs and auth security audit events

## Conclusion

The project has a substantial implemented authentication and authorization foundation for IT16, including account recovery, email confirmation, optional Authenticator App MFA, optional Email OTP MFA, recovery codes, lockout, adaptive login reCAPTCHA, rate limiting, audit logging, PayMongo webhook signature validation, and CI-backed security-tooling evidence. The most important remaining hardening items are stronger token lifecycle controls, production-grade/shared Email OTP challenge storage, and tightening CI security enforcement thresholds after baseline remediation.
