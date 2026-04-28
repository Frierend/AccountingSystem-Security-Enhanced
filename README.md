# AccSys: Web-Based Integrated Accounting and Financial Management System

<p align="center">
  <img src="AccountingSystem.Client/wwwroot/AccsysLogo.png" alt="AccSys Logo" width="150" height="150"/>
</p>

> A web-based accounting system for multi-tenant bookkeeping, reporting, and controlled financial operations.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-512BD4?style=for-the-badge&logo=blazor&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![SQL Server](https://img.shields.io/badge/SQL_Server-2022-CC2927?style=for-the-badge&logo=microsoft-sql-server&logoColor=white)](https://www.microsoft.com/sql-server)

## Overview

**AccSys** is a .NET 8 accounting platform that supports General Ledger (GL), Accounts Payable (AP), Accounts Receivable (AR), tenant administration, and audit tracking.

## Key Features

- Automated double-entry postings for core accounting flows.
- Bills and invoices lifecycle management.
- Financial statements and PDF export.
- Role-based UI and API access control.
- Audit log visibility for tenant and super-admin actions.
- PayMongo payment source and redirect flow (test-mode usage for local/academic demonstration).
- Registration flow protected by Google reCAPTCHA v2 Checkbox.
- Password reset and email confirmation flows through SMTP.

## System Architecture

### Technology Stack

#### Frontend
- Blazor WebAssembly (.NET 8)
- MudBlazor UI components
- Protected routing with `AuthorizeRouteView`

#### Backend
- ASP.NET Core 8 Web API
- Entity Framework Core 8
- Microsoft SQL Server
- JWT authentication and role-based authorization
- SMTP email delivery (`SmtpClient`) with Gmail App Password compatible configuration

#### Integrations
- Google reCAPTCHA v2 Checkbox (registration)
- PayMongo API (test mode for project demonstration)
- QuestPDF for reporting
- World Bank and Frankfurter APIs for economic/rate data

## Security Features

- JWT-based authentication and role-based authorization.
- Password security uses ASP.NET Core Identity password hashing for provisioned Identity accounts, with a legacy compatibility path for existing legacy password data.
- Shared password policy (`AccountingSystem.Shared/Validation/PasswordPolicy.cs`):
  - complex password: minimum 12 characters with at least 3 of 4 classes (uppercase, lowercase, number, symbol), or
  - passphrase: minimum 16 characters with at least 3 words.
  - maximum length: 128 characters.
- Login protection defaults:
  - lockout after 5 failed attempts
  - temporary demo lockout duration: 5 minutes
  - adaptive login reCAPTCHA after repeated failed attempts
  - endpoint-specific rate limiting on auth routes.
- Optional MFA:
  - Authenticator App MFA with recovery codes
  - Email OTP MFA to a confirmed email address
- Tenant isolation through middleware and EF Core query filters.
- Audit logging through `AuditMiddleware` plus dedicated auth security events, including mirrored SuperAdmin-account auth events in governance logs.

## Security Policy Snapshot

- **Secure configuration policy**: runtime secrets are expected from environment variables or `.env` in local development. Checked-in `appsettings.json` uses placeholders (`__SET_VIA_ENV__`) for sensitive values.
- **Authentication policy**: login, forgot password, reset password, email confirmation, resend confirmation, optional Authenticator App MFA, optional Email OTP MFA, and recovery codes are implemented.
- **Authorization policy**: roles include `Admin`, `Accounting`, `Management`, and `SuperAdmin`; protected pages and API endpoints enforce role checks.
- **Data handling policy**: passwords are not stored in plaintext; EF Core is used for database access; HTTPS redirection is enabled in API startup.
- **Monitoring policy**: security-relevant events are logged through audit tables and auth-security audit events.

## Code Auditing Tooling Evidence (CI)

- CI workflow `.github/workflows/security-tooling-evidence.yml` provides:
  - solution restore/build
  - API and client test execution
  - dependency vulnerability report generation (`dotnet list package --vulnerable --include-transitive`) with uploaded artifact evidence
  - gitleaks secret scan execution in report-first/non-blocking mode for evidence collection
- CI workflow `.github/workflows/codeql.yml` provides:
  - GitHub CodeQL static analysis for C# on push, pull request, and weekly schedule
- Current posture is evidence-first and non-invasive. Enforcement gates can be tightened after baseline triage and remediation.

## Known Limitations and Recommended Improvements

- **Known Limitation**: Email OTP challenges are stored in memory for this demo build; pending codes are lost if the API restarts.
- **Known Limitation**: JWT is stored in browser local storage; this increases token theft impact if XSS is introduced.
- **Recommended Improvement**: continue standardizing auth error responses to safer response envelopes where legacy endpoints still use broad `BadRequest` handling.
- **Recommended Improvement**: increase CI security enforcement from report-first mode to policy gates after vulnerability/secret baseline remediation.
- **Recommended Improvement**: add refresh token and token revocation strategy for stronger session control.

## User Roles and Permissions

### Admin
- Full tenant administration.
- User management and company settings.
- Access to accounting transaction modules and reports.

### Accounting
- GL/AP/AR transaction workflows.
- Financial dashboard and report access.
- No super-admin functions.

### Management
- Dashboard and reporting visibility.
- Limited transaction capabilities based on endpoint/page role attributes.

### SuperAdmin
- Cross-tenant governance endpoints and super-admin audit logs.

## Core Modules

1. User and role management
2. General Ledger (accounts, journals, trial balance)
3. Accounts Payable (vendors, bills, outgoing payments)
4. Accounts Receivable (customers, invoices, incoming payments)
5. Financial reporting and PDF generation

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server
- Visual Studio 2022 or equivalent .NET tooling
- Modern browser

### Installation

1. Clone the repository.
2. Configure required secrets using `.env` and/or environment variables.
3. Start API project.
4. Start client project.

See `SECURITY_CONFIGURATION.md` for required keys and safe secret-handling policy.

## Project Objectives

- Centralize GL/AP/AR accounting operations.
- Improve accounting traceability through audit logs.
- Enforce role-based and tenant-aware access control.
- Support secure authentication flows suitable for academic security evaluation.

## Developer

**Adzyl Hilary A. Jipos**  
BS Information Technology Student  
University of Mindanao, Davao City

## Support

For issues and clarifications, open a repository issue or coordinate with the project owner.
