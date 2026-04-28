# HARDENING_NOTES

## Implemented Hardening Controls

The following controls are implemented in the current codebase:

1. Shared password policy validation for registration and password-change paths.
2. Account lockout tracking with configurable defaults (5 failed attempts, 15-minute lockout).
3. Endpoint-level auth rate limiting (login, register-company, forgot/reset password, confirm/resend confirmation, MFA routes).
4. JWT validation settings with explicit issuer, audience, signing key, expiry, and configurable clock skew.
5. Auth security event logging through dedicated service-level audit writes.
6. Sanitized non-auth request logging in middleware for state-changing operations.
7. CI security-tooling evidence via GitHub Actions:
   - `.github/workflows/security-tooling-evidence.yml` (build/test, dependency vulnerability report artifact, gitleaks report-first secret scan)
   - `.github/workflows/codeql.yml` (CodeQL static analysis for C#)

## Logging and Monitoring Policy

- Tenant-level business mutation events are captured in `AuditLogs`.
- Auth and account-security events are captured through `AuthSecurityAuditService`.
- Super-admin governance actions are captured in `SuperAdminAuditLogs`.
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

- **Known Limitation:** PayMongo webhook signature verification still requires cryptographic hardening.
- **Known Limitation:** JWT tokens are stored in browser local storage, which increases risk if XSS exists.
- **Known Limitation:** Some auth endpoints still return raw exception messages in API responses.

## Recommended Improvements

- **Recommended Improvement:** implement strict webhook signature validation and replay protection for payment webhooks.
- **Recommended Improvement:** normalize auth error responses to standardized sanitized error contracts.
- **Recommended Improvement:** add refresh-token and revocation capabilities for stronger token lifecycle control.
- **Recommended Improvement:** tighten CI security policy gates after baseline triage (for example, fail-on-severity thresholds for dependency/secret findings).

## Evidence Checklist

- [x] Password policy enforcement in shared validation layer
- [x] Lockout policy enforcement with configurable defaults
- [x] Auth endpoint rate-limiting policies
- [x] Auth security audit events
- [x] Tenant and super-admin audit logging
- [x] JWT validation configuration and middleware path
- [x] CI security tooling evidence (build/test, dependency scan report, secret scan evidence, CodeQL)
- [ ] Webhook signature verification hardening (remaining work)
