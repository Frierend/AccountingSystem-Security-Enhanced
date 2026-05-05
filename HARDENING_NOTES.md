# HARDENING_NOTES

## Implemented Hardening Controls

The following controls are implemented in the current codebase:

1. Shared password policy validation for registration and password-change paths.
2. Account lockout tracking with configurable defaults (5 failed attempts, 5-minute demo lockout).
3. Endpoint-level auth rate limiting (login, register-company, forgot/reset password, confirm/resend confirmation, MFA routes).
4. JWT validation settings with explicit issuer, audience, signing key, expiry, and configurable clock skew.
5. Auth security event logging through dedicated service-level audit writes.
6. Sanitized non-auth request logging in middleware for state-changing operations.
7. Optional independently managed Authenticator App MFA, Email OTP MFA, and recovery codes.
8. PayMongo webhook HMAC signature validation with replay-window checking.
9. Google reCAPTCHA v2 Checkbox for registration and every normal login attempt, with public site key and server secret read from configuration.
10. Backup SuperAdmin management with strong-password creation, email confirmation flow, and last-active-SuperAdmin protection.
11. CI security-tooling evidence via GitHub Actions:
   - `.github/workflows/security-tooling-evidence.yml` (build/test, dependency vulnerability report artifact, gitleaks report-first secret scan)
   - `.github/workflows/codeql.yml` (CodeQL static analysis for C#)

## Logging and Monitoring Policy

- Tenant-level business mutation events are captured in `AuditLogs`.
- Tenant audit logs display System and Security categories.
- Auth and account-security events are captured through `AuthSecurityAuditService`.
- Super-admin governance actions are captured in `SuperAdminAuditLogs`.
- SuperAdmin-account login failures, lockouts, CAPTCHA-required events, MFA challenges, and successful logins are mirrored into `SuperAdminAuditLogs`.
- Backup SuperAdmin creation, enable/disable actions, and attempts to disable the last active SuperAdmin are captured in `SuperAdminAuditLogs`.
- The seeded/demo bootstrap SuperAdmin is email-confirmed for MFA demonstration; backup SuperAdmins follow the normal confirmation workflow.
- OTP values, recovery codes, CAPTCHA tokens, passwords, JWTs, and secrets are not written to audit details.
- Development email fallback logs reset/confirmation links when SMTP is not configured.

## Incident Response Plan

### 1. Detection

- Monitor API logs, auth audit events, and payment/audit anomalies.
- Identify unusual login failure spikes, lockout patterns, and unauthorized status transitions.

### 2. Reporting

- Record incident time, affected tenant(s), endpoint(s), and observed impact.
- Preserve relevant audit log records and application logs.

### 3. Containment

- Rotate exposed secrets (JWT, SMTP, PayMongo, reCAPTCHA, database credentials) as needed.
- Temporarily disable vulnerable flows when required.
- Block or restrict compromised accounts and tenant access where appropriate.

### 4. Recovery

- Restore validated configuration values.
- Re-test authentication, authorization, and payment flows.
- Confirm audit logging remains functional after remediation.

### 5. Post-Incident Review

- Document root cause and corrective actions.
- Track residual risks as known limitations or planned improvements.
- Update submission documentation with verified security status.

## Known Limitations

- **Known Limitation:** JWT tokens are stored in browser local storage, which increases risk if XSS exists.
- **Known Limitation:** Email OTP challenges are stored in memory for this demo build; pending codes are lost if the API restarts.

## Recommended Improvements

- **Recommended Improvement:** normalize auth error responses to standardized sanitized error contracts.
- **Recommended Improvement:** use database or distributed-cache backed Email OTP challenge storage for production or multi-instance deployments.
- **Recommended Improvement:** add refresh-token and revocation capabilities for stronger token lifecycle control.
- **Recommended Improvement:** tighten CI security policy gates after baseline triage (for example, fail-on-severity thresholds for dependency/secret findings).

## Evidence Checklist

- [x] Password policy enforcement in shared validation layer
- [x] Lockout policy enforcement with configurable defaults
- [x] Auth endpoint rate-limiting policies
- [x] Auth security audit events
- [x] Tenant and super-admin audit logging
- [x] SuperAdmin-account auth event mirroring
- [x] JWT validation configuration and middleware path
- [x] Registration and always-on login reCAPTCHA
- [x] Configuration-based reCAPTCHA public site key and server-only secret key handling
- [x] Independent Authenticator App and Email OTP MFA management
- [x] Backup SuperAdmin support and last-active protection
- [x] CI security tooling evidence (build/test, dependency scan report, secret scan evidence, CodeQL)
- [x] PayMongo webhook signature verification hardening
