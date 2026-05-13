# IT16/L Information Security 1 Documentation

## Project Overview

This project is developed in partial fulfillment of the requirements for IT 16/L - Information Security 1.
This documentation presents the design, implementation, and security considerations of the proposed system.

Prepared by: Cyril John Atillo
Submitted to: Cyril Loyd Tomas

## System Description

AccSys is a web-based accounting and financial management system. It supports company and tenant records, users, invoices, bills, journal entries, payments, reports, and audit logs.

The system uses authentication, role-based access control, MFA options, Google reCAPTCHA, account lockout, rate limiting, audit logs, and secure configuration practices. These controls support safer access, abuse reduction, and activity review.

## Platform and Technologies Used

- Programming Language: C#
- Framework / Environment: ASP.NET Core 8 Web API, Blazor WebAssembly, .NET 8
- Database: SQL Server with Entity Framework Core
- Platform: Web-based system
- Security / Integrations: ASP.NET Core Identity, JWT authentication, Google reCAPTCHA v2 Checkbox, SMTP email service, PayMongo test integration, GitHub Actions, CodeQL, Gitleaks, dependency vulnerability reporting

## Security Policies

- Password Policy:
  - Strong passwords are enforced using the implemented Identity password policy.
  - Passwords are hashed through ASP.NET Core Identity.
  - Password reset is supported through email.
  - Periodic password expiration is listed as a recommended improvement.

- Login Attempt Policy:
  - Failed login attempts are tracked.
  - Accounts are temporarily locked after exceeding the configured failed attempt limit.
  - The current demo lockout duration is 5 minutes.
  - Login reCAPTCHA is visible and required.
  - Rate limiting is applied to sensitive authentication-related endpoints.
  - These controls help reduce repeated automated login attempts.

- Data Handling Policy:
  - Passwords are hashed.
  - OTPs, recovery codes, CAPTCHA tokens, JWTs, and secrets must not be logged.
  - Runtime secrets are stored through environment variables or local .env configuration.
  - .env is not committed.
  - .env.example is only a template.

- Access Control Policy:
  - RBAC is used.
  - Tenant/company users access only authorized company resources.
  - SuperAdmin users manage platform-level governance.
  - The system prevents disabling/deleting the last active SuperAdmin.
  - Sensitive SuperAdmin actions require step-up verification.
  - Backup SuperAdmin support exists.

- Logging and Monitoring Policy:
  - Tenant audit logs record company-level system/security activities.
  - SuperAdmin audit logs record platform-level governance/security events.
  - Logs must not contain passwords, OTPs, recovery codes, CAPTCHA tokens, JWTs, or secrets.
  - Suspicious activity can be reviewed through audit logs.

Application-layer controls such as reCAPTCHA, rate limiting, account lockout, and audit logs help reduce abuse. Full DoS/DDoS protection also requires deployment-level controls such as firewall, reverse proxy limits, WAF/CDN protection, monitoring, and alerting.

## Incident Response Plan

- Detection:
  Detection is supported through audit logs, security events, GitHub Actions, CodeQL, Gitleaks, and administrator review.

- Reporting:
  Incidents should be reported to the system administrator, SuperAdmin, or responsible authority.

- Response:
  Response actions may include account lockout, disabling suspicious accounts, password reset, using a backup SuperAdmin if needed, reviewing logs, and rotating exposed secrets if needed.

- Recovery:
  Recovery includes restoring access, verifying data integrity, re-enabling safe accounts, and confirming that security controls are working.

- Review:
  A post-incident review should identify the cause, record lessons learned, and list future improvements.

## Code Auditing and Security Review

- Tool Used:
  CodeQL, GitHub Actions Security Tooling Evidence workflow, Gitleaks, dependency vulnerability reporting, .NET build/test tools.

- Usage:
  CodeQL was used as the project's code auditing/static security analysis tool through GitHub Actions. The other tools are used to provide build/test evidence, secret scanning evidence, and dependency vulnerability report evidence.

- Findings:
  - CI project reference casing issue was fixed.
  - Public reCAPTCHA site key was treated as client-side public value.
  - Real secrets are kept out of source control.
  - Existing analyzer warnings, if any, are not build blockers.

- Fixes:
  - CI reference casing corrected.
  - Gitleaks allowlist limited to public reCAPTCHA site key only when needed.
  - Secrets remain server-side or in environment/local .env.
  - Security controls such as reCAPTCHA, MFA, account lockout, audit logs, and step-up verification were documented and tested.

- Proof:
  [Insert Screenshot: GitHub Actions Security Tooling Evidence green check]
  [Insert Screenshot: CodeQL green check]
  [Insert Screenshot: Local API test result]
  [Insert Screenshot: Local Client test result]
  [Insert Screenshot: Secret scan / Gitleaks evidence]

## Access Control (RBAC / ACL)

## Intended Users

- Guest User - Can access login and company registration pages.
- Authorized Company User - Can access assigned accounting modules based on role permissions.
- Tenant Admin / Company Administrator - Manages company users, company settings, accounting records, financial reports, and tenant audit logs.
- SuperAdmin / System Administrator - Manages tenants, global users, backup SuperAdmin accounts, governance actions, and SuperAdmin audit logs.

## Access Control Matrix

| Feature / Resource | Guest | Company User | Tenant Admin | SuperAdmin |
| --- | --- | --- | --- | --- |
| View Login Page | Allowed | Allowed | Allowed | Allowed |
| Company Registration | Allowed | Denied | Denied | Denied |
| Login | Allowed | Allowed | Allowed | Allowed |
| Dashboard | Denied | Allowed | Allowed | Allowed |
| Edit Profile | Denied | Allowed | Allowed | Allowed |
| Enable/Manage MFA | Denied | Allowed | Allowed | Allowed |
| Manage Company Users | Denied | Denied | Allowed | Role-based |
| Manage Company Settings | Denied | Denied | Allowed | Role-based |
| Chart of Accounts | Denied | Role-based | Allowed | Denied |
| Journal Entries | Denied | Role-based | Allowed | Denied |
| Bills | Denied | Role-based | Allowed | Denied |
| Invoices | Denied | Role-based | Allowed | Denied |
| Payments | Denied | Role-based | Allowed | Denied |
| Financial Statements | Denied | Role-based | Allowed | Denied |
| Tenant Audit Logs | Denied | Denied | Allowed | Denied |
| Tenant Manager | Denied | Denied | Denied | Allowed |
| Global Users | Denied | Denied | Denied | Allowed |
| Backup SuperAdmin Management | Denied | Denied | Denied | Allowed |
| SuperAdmin Audit Logs | Denied | Denied | Denied | Allowed |
| Sensitive SuperAdmin Actions | Denied | Denied | Denied | Allowed |
| Disable/Delete Users or Records | Denied | Role-based | Allowed | Role-based |

## Evidence Placeholders

[Insert Screenshot: Login page with reCAPTCHA checkbox]
[Insert Screenshot: Login lockout message showing temporary lockout]
[Insert Screenshot: Registration page with reCAPTCHA checkbox]
[Insert Screenshot: User Profile showing Authenticator App MFA and Email OTP MFA]
[Insert Screenshot: MFA login page showing Authenticator App, Email OTP, and Recovery Code options]
[Insert Screenshot: Tenant audit logs showing System and Security categories]
[Insert Screenshot: SuperAdmin audit logs showing governance/security events]
[Insert Screenshot: SuperAdmin audit details dialog]
[Insert Screenshot: Step-up verification dialog for sensitive SuperAdmin action]
[Insert Screenshot: Global User Management showing backup SuperAdmin support]
[Insert Screenshot: GitHub Actions green checks for Security Tooling Evidence and CodeQL]
[Insert Screenshot: Local API and Client test results]
[Insert Screenshot: PayMongo test payment/callback evidence, if included in presentation]
