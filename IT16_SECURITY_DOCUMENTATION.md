# IT16/L Information Security 1 Documentation

## Project Overview
This project is developed in partial fulfillment of the requirements for IT 16/L - Information Security 1.
This documentation presents the design, implementation, and security considerations of the proposed system.

Prepared by: Cyril John Atillo
Submitted to: Cyril Loyd Tomas

## System Description
AccSys is a web-based integrated accounting and financial management system for multi-tenant company bookkeeping, reporting, payment tracking, user management, and audit monitoring. The system supports General Ledger, Accounts Payable, Accounts Receivable, financial reports, PayMongo test payment flow, tenant administration, and SuperAdmin governance while applying authentication, authorization, MFA, reCAPTCHA, account lockout, rate limiting, tenant isolation, and audit logging controls.

## Platform and Technologies Used
- Programming Language: C#
- Framework / Environment: ASP.NET Core 8 Web API, Blazor WebAssembly, .NET 8, MudBlazor
- Database: Microsoft SQL Server with Entity Framework Core
- Platform: Web-based client and API system
- Security / Integrations: ASP.NET Core Identity, JWT, Google reCAPTCHA v2 Checkbox, Authenticator App TOTP MFA, Email OTP MFA, recovery codes, SMTP email delivery, PayMongo test mode, GitHub Actions, CodeQL, and Gitleaks

## Security Policies
- Password Policy:
  - Enforces strong passwords.
  - Stores passwords as hashed values.
  - Supports secure password reset.
  - Periodic password update is a recommended improvement only because forced periodic password expiration is not currently implemented.

- Login Attempt Policy:
  - Limits failed attempts.
  - Locks accounts after exceeding the configured failed-attempt limit.
  - Uses a 5-minute demo lockout.
  - Shows a safe temporary-lockout message for locked existing accounts, including approximate remaining minutes for demo guidance.
  - Does not show exact attempts left.
  - Login reCAPTCHA is always visible and required.
  - Registration reCAPTCHA is required.
  - Rate limiting is applied to sensitive auth endpoints including login, register company, forgot password, reset password, email confirmation, resend confirmation, MFA login, Email OTP flows, MFA management, and SuperAdmin step-up Email OTP send.

- Data Handling Policy:
  - Passwords are hashed.
  - OTPs, recovery codes, CAPTCHA tokens, JWTs, API keys, SMTP passwords, PayMongo keys, and other secrets are not logged.
  - Runtime secrets are stored in environment variables or local `.env` only.
  - Checked-in `appsettings.json` uses placeholders for sensitive values.
  - `.env.example` remains tracked as a safe template.

- Access Control Policy:
  - Uses RBAC.
  - Enforces tenant/company access restrictions.
  - Restricts tenant records by company ownership.
  - SuperAdmin governance is separated from tenant operations.
  - Sensitive SuperAdmin actions require step-up verification.
  - The system prevents disabling or deleting the last active SuperAdmin account.

- Logging and Monitoring Policy:
  - Tenant audit logs record company-level system and security activity.
  - SuperAdmin governance audit logs record platform-level administrative actions.
  - Security events include login failures, CAPTCHA checks, account lockouts, MFA challenges, step-up verification, and rate-limit rejections.
  - Suspicious activity should be reviewed by authorized administrators.

- Session Security Notes:
  - JWT is stored in browser local storage.
  - A user may remain logged in after local app restart until token expiration or manual logout because restarting the API/client does not automatically clear browser storage.
  - Logout clears the client token.
  - Current JWT expiry is configured at 60 minutes.
  - Recommended production improvements include refresh-token/session revocation, server-side token invalidation, and shorter token lifetime where appropriate.

- Application-Layer Abuse Protection Policy:
  - Application-level controls include login reCAPTCHA, registration reCAPTCHA, failed login tracking, account lockout, rate limiting, and audit logs.
  - These controls help reduce brute-force attempts and automated abuse.
  - These controls do not fully stop network-level DoS or DDoS attacks.
  - Full DoS/DDoS protection requires deployment and infrastructure controls such as reverse proxy rate limiting, firewall/WAF, CDN or cloud DDoS protection, monitoring, and alerting.

## Incident Response Plan
- Detection:
Security incidents are detected through tenant audit logs, SuperAdmin audit logs, authentication security events, rate-limit events, GitHub Actions results, CodeQL findings, Gitleaks evidence, and administrator monitoring.

- Reporting:
Incidents should be reported to the Tenant Admin, SuperAdmin/System Administrator, instructor, or responsible authority with the affected account, tenant, endpoint, time window, and observed behavior.

- Response:
Response actions may include disabling or restricting affected accounts, using a trusted backup SuperAdmin to review governance logs, forcing password reset, rotating exposed secrets, reviewing rate-limit and lockout events, applying code fixes, and preserving evidence screenshots/logs.

- Recovery:
Restore safe configuration, verify database and tenant data integrity, confirm login, MFA, email confirmation, reset password, PayMongo, and audit-log flows still work, then re-enable affected accounts only after validation.

- Review:
Review the root cause, confirm whether sensitive data was exposed, update documentation, add or adjust tests, improve monitoring, and record proof for IT16 submission.

## Code Auditing and Security Review
- Tool Used:
GitHub Actions Security Tooling Evidence workflow, CodeQL, Gitleaks, dependency vulnerability reporting, .NET build/test tools, `git status`, `git ls-files`, `.gitignore` review, and manual code inspection.

- Usage:
The tools are used to restore, build, test, scan source code, collect dependency vulnerability evidence, inspect secret hygiene, confirm rate-limit coverage, and verify security-sensitive behavior before documentation submission.

- Findings:
Manual review found that locked login attempts previously used the same generic credential message, which made demo lockout behavior unclear. Rate limiting already protected the main auth endpoints, and SuperAdmin step-up Email OTP send was added to the authenticated MFA-management rate-limit policy. Browser local storage explains why a JWT session can persist after a local app restart. Application-layer controls reduce abuse but do not fully prevent DDoS.

- Fixes:
Login lockout responses were updated to use a safe temporary-lockout message with approximate remaining minutes. Sensitive auth endpoint rate-limit coverage was confirmed and SuperAdmin step-up Email OTP send was protected. `.gitignore` was tightened for local secrets and generated artifacts. Documentation was updated to avoid overclaiming DoS/DDoS protection and to explain JWT/local storage session persistence.

- Proof:
[Insert Screenshot: Login page with reCAPTCHA]
[Insert Screenshot: Locked login message showing temporary lockout and approximate minutes]
[Insert Screenshot: GitHub Actions Security Tooling Evidence green check]
[Insert Screenshot: CodeQL green check]
[Insert Screenshot: Local API test result showing passing tests]
[Insert Screenshot: Local Client test result showing passing tests]
[Insert Screenshot: Gitleaks or secret scan evidence]
[Insert Screenshot: git status and git diff check evidence]

## Access Control (RBAC / ACL)
AccSys uses role-based access control and tenant access restrictions. Guest users can access public authentication pages only. Authorized company users access tenant features according to role permissions. Tenant Admins manage company users, settings, records, reports, and tenant audit logs. SuperAdmin/System Administrator users manage platform-level governance, tenants, global users, backup SuperAdmins, and SuperAdmin audit logs.

## Intended Users
- Guest User
- Authorized Company User
- Tenant Admin / Company Administrator
- SuperAdmin / System Administrator

## Access Control Matrix
| System Feature / Resource | Guest User | Authorized Company User | Tenant Admin / Company Administrator | SuperAdmin / System Administrator |
| --- | --- | --- | --- | --- |
| View Login Page | Allowed | Allowed | Allowed | Allowed |
| Company Registration | Allowed | Denied | Denied | Denied |
| Forgot / Reset Password | Allowed | Allowed | Allowed | Allowed |
| Email Confirmation / Resend Confirmation | Allowed | Allowed | Allowed | Allowed |
| Login with reCAPTCHA | Allowed | Allowed | Allowed | Allowed |
| MFA Login | Denied | Allowed when enabled | Allowed when enabled | Allowed when enabled |
| User Dashboard | Denied | Allowed | Allowed | Allowed |
| Edit Own Profile | Denied | Allowed | Allowed | Allowed |
| Enable Authenticator App MFA | Denied | Allowed | Allowed | Allowed |
| Enable Email OTP MFA | Denied | Allowed after email confirmation | Allowed after email confirmation | Allowed after email confirmation |
| Use Recovery Codes | Denied | Allowed when generated | Allowed when generated | Allowed when generated |
| Manage Company Users | Denied | Denied | Allowed | Role-based governance only |
| Manage Company Settings | Denied | Denied | Allowed | Role-based governance only |
| Manage Chart of Accounts | Denied | Role-based | Allowed | Denied |
| Manage Journal Entries | Denied | Role-based | Allowed | Denied |
| Manage Bills and Vendors | Denied | Role-based | Allowed | Denied |
| Manage Invoices and Customers | Denied | Role-based | Allowed | Denied |
| Receive Payments | Denied | Role-based | Allowed | Denied |
| View Financial Reports | Denied | Role-based | Allowed | Denied |
| View Tenant Audit Logs | Denied | Denied | Allowed | Denied |
| Manage Tenants | Denied | Denied | Denied | Allowed |
| View Global Users | Denied | Denied | Denied | Allowed |
| Manage Backup SuperAdmins | Denied | Denied | Denied | Allowed with step-up verification |
| Send SuperAdmin Step-Up Email OTP | Denied | Denied | Denied | Allowed with rate limiting |
| View SuperAdmin Audit Logs | Denied | Denied | Denied | Allowed |
| System Configuration / Security Governance | Denied | Denied | Denied | Allowed |
| Delete / Disable Records or Users | Denied | Role-based | Allowed | Role-based governance only |

## Evidence Placeholders
[Insert Screenshot: Login page with reCAPTCHA]
[Insert Screenshot: Registration reCAPTCHA]
[Insert Screenshot: User Profile with TOTP and Email OTP MFA]
[Insert Screenshot: MFA login page]
[Insert Screenshot: Tenant audit logs]
[Insert Screenshot: SuperAdmin audit logs]
[Insert Screenshot: Step-up verification dialog]
[Insert Screenshot: GitHub Actions green checks]
[Insert Screenshot: API/client test results]
