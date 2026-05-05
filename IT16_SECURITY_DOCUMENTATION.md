# IT16/L Information Security 1 Documentation

## Project Overview

This project is developed in partial fulfillment of the requirements for IT 16/L – Information Security 1. This documentation presents the design, implementation, and security considerations of the proposed system.

Prepared by: Cyril John Atillo
Submitted to: Cyril Loyd Tomas

## System Description

The system is designed to manage and monitor accounting and financial management operations. It enables authorized users to manage company records, users, invoices, bills, journal entries, payments, and financial reports, while ensuring data security through implemented policies and access control mechanisms such as authentication, role-based access control, multi-factor authentication, reCAPTCHA verification, account lockout, audit logging, and secure configuration.

## Platform and Technologies Used

- Programming Language: C#
- Framework / Environment: ASP.NET Core 8 Web API, Blazor WebAssembly, .NET 8
- Database: SQL Server with Entity Framework Core
- Platform: Web-based system
- Security / Integrations: ASP.NET Core Identity, JWT, Google reCAPTCHA v2 Checkbox, Gmail SMTP/App Password-compatible SMTP, PayMongo test mode, GitHub Actions, CodeQL, and Gitleaks

## Security Policies

- Password Policy:
  - Enforces strong passwords using the system password policy.
  - Requires password complexity such as minimum length, uppercase letters, lowercase letters, numbers, or symbols/passphrase rules based on the implemented policy.
  - Stores passwords as hashed values through ASP.NET Core Identity.
  - Supports secure password reset through email.
  - Note: Periodic password expiration is listed as a recommended improvement because it is not currently implemented.

- Login Attempt Policy:
  - Limits failed login attempts through server-side tracking.
  - Temporarily locks accounts after exceeding the configured maximum failed attempts.
  - Uses a 5-minute lockout duration for the current demo configuration.
  - Requires Google reCAPTCHA verification on the login page.
  - Uses a client-side public reCAPTCHA site key in the Blazor auth pages; the secret key remains server-side through environment/configuration.
  - Does not show exact attempts left or countdown by default to reduce attacker feedback.

- Data Handling Policy:
  - Sensitive data is protected through hashing, secure configuration, and access restrictions.
  - Passwords are hashed and are not stored in plaintext.
  - OTP values, recovery codes, CAPTCHA tokens, JWTs, and secrets are not logged.
  - Email OTP challenges are short-lived, one-time use, and stored securely for the demo implementation.
  - Email OTP MFA requires a confirmed email address. SuperAdmin users can resend confirmation before enabling Email OTP MFA.
  - Email OTP MFA and Authenticator App MFA are independent profile-managed methods.
  - Runtime secrets are stored in environment variables or local .env files and are not committed to the repository.

- Access Control Policy:
  - System access is restricted through role-based access control.
  - Tenant/company users can only access authorized company-level resources.
  - Tenant administrators manage company-level users, records, and audit logs.
  - SuperAdmin/System Administrator users manage platform-level governance, backup SuperAdmin accounts, and SuperAdmin audit logs.
  - The system prevents disabling or deleting the last active SuperAdmin account.
  - Sensitive SuperAdmin governance actions require step-up verification and are recorded in SuperAdmin audit logs.
  - System configuration and sensitive administrative actions are restricted to authorized roles.

- Logging and Monitoring Policy:
  - System activities such as logins, MFA actions, Email OTP events, record changes, and security-related events are recorded.
  - Tenant audit logs display System and Security categories.
  - SuperAdmin governance audit logs display platform-level and SuperAdmin-related security events.
  - SuperAdmin audit logs include login failure, CAPTCHA required, login success, lockout, backup SuperAdmin creation, enable/disable, step-up verification, and last-active protection events.
  - Logs are reviewed for suspicious activity and must not contain passwords, OTPs, recovery codes, CAPTCHA tokens, JWTs, or secrets.

## Incident Response Plan

- Detection:
Security incidents are identified through system logs, audit logs, GitHub Actions security checks, CodeQL analysis, Gitleaks scanning, and administrator monitoring.

- Reporting:
Incidents are reported to the system administrator, SuperAdmin, or responsible authority immediately.

- Response:
Immediate actions are taken to contain and mitigate the issue, such as account lockout, disabling compromised accounts, using a trusted backup SuperAdmin to review governance logs, resetting credentials, rotating exposed secrets, and applying code fixes.

- Recovery:
Restore system functionality, verify data integrity, re-enable safe accounts, and confirm that security controls are working properly.

- Review:
Conduct post-incident analysis to identify the cause of the incident and improve future security measures, policies, and monitoring.

## Code Auditing and Security Review

- Tool Used:
GitHub Actions Security Tooling Evidence workflow, CodeQL, Gitleaks, dependency vulnerability reporting, .NET build/test tools.

- Usage:
The tools were used to scan and verify the project through automated build and test execution, static code analysis, secret scanning, and dependency vulnerability reporting.

- Findings:
Previous CI issues involved project reference casing and public reCAPTCHA site key handling. These were addressed by allowing only the public reCAPTCHA site key constants in client auth pages while keeping secret keys out of source control. Existing non-blocking warnings may remain, such as UI analyzer warnings, but they do not stop the build or tests.

- Fixes:
CI project reference casing was corrected, the public reCAPTCHA site key was limited to auth-page client constants, secret values are kept in server-side configuration and out of source control, and security features such as safe error handling, audit log sanitization, MFA, and reCAPTCHA were implemented.

- Proof:
[Insert Screenshot: GitHub Actions Security Tooling Evidence green check]
[Insert Screenshot: CodeQL green check]
[Insert Screenshot: Local API test result showing passing tests]
[Insert Screenshot: Local Client test result showing passing tests]
[Insert Screenshot: Gitleaks/secret scan evidence or workflow result]

## Access Control (RBAC / ACL)

## Intended Users

- Guest User – Can access the login page and company registration page only.
- Authorized Company User – Can access assigned accounting modules and records based on role permissions.
- Tenant Admin / Company Administrator – Manages company users, company settings, accounting records, invoices, payments, financial reports, and tenant audit logs.
- SuperAdmin / System Administrator – Manages platform-level tenants, global users, backup SuperAdmin accounts, governance actions, and SuperAdmin audit logs.

## Access Control Matrix

| System Feature / Resource | Guest User | Authorized Company User | Tenant Admin | SuperAdmin |
| --- | --- | --- | --- | --- |
| View Login Page | Allowed | Allowed | Allowed | Allowed |
| User Registration / Company Registration | Allowed | Denied | Denied | Denied |
| Login | Allowed | Allowed | Allowed | Allowed |
| User Dashboard | Denied | Allowed | Allowed | Allowed |
| Edit Profile | Denied | Allowed | Allowed | Allowed |
| Enable MFA | Denied | Allowed | Allowed | Allowed |
| Manage Company Users | Denied | Denied | Allowed | Role-based |
| Manage Company Settings | Denied | Denied | Allowed | Role-based |
| Manage Chart of Accounts | Denied | Role-based | Allowed | Denied |
| Manage Journal Entries | Denied | Role-based | Allowed | Denied |
| Manage Bills | Denied | Role-based | Allowed | Denied |
| Manage Invoices | Denied | Role-based | Allowed | Denied |
| Receive Payments | Denied | Role-based | Allowed | Denied |
| View Financial Statements | Denied | Role-based | Allowed | Denied |
| View Tenant Audit Logs | Denied | Denied | Allowed | Denied |
| Manage Tenants | Denied | Denied | Denied | Allowed |
| View Global Users | Denied | Denied | Denied | Allowed |
| Manage Backup SuperAdmins | Denied | Denied | Denied | Allowed |
| View SuperAdmin Audit Logs | Denied | Denied | Denied | Allowed |
| System Configuration / Security Governance | Denied | Denied | Denied | Allowed |
| Delete / Disable Records or Users | Denied | Role-based | Allowed | Role-based |

## Evidence Placeholders

[Insert Screenshot: Login page with reCAPTCHA checkbox]
[Insert Screenshot: Registration page with reCAPTCHA checkbox]
[Insert Screenshot: User Profile showing Authenticator App MFA and Email OTP MFA]
[Insert Screenshot: MFA login page showing Authenticator App, Email OTP, and Recovery Code options]
[Insert Screenshot: Tenant audit logs showing System and Security categories]
[Insert Screenshot: SuperAdmin audit logs showing governance/security events]
[Insert Screenshot: Global User Management showing backup SuperAdmin support]
[Insert Screenshot: PayMongo test payment/callback evidence]
[Insert Screenshot: GitHub Actions green checks for Security Tooling Evidence and CodeQL]
