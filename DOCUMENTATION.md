# PROJECT_DOCUMENTATION — AccountingSystem

## 1. System Architecture Overview

### 1.1 Overall architecture pattern

AccountingSystem follows a **3-project layered architecture**:

- **AccountingSystem.Client**: Blazor WebAssembly SPA (presentation/UI layer).
- **AccountingSystem.Api**: ASP.NET Core Web API (application/service layer).
- **AccountingSystem.Shared**: shared contracts (DTOs, enums) consumed by both client and API.

It is effectively a **client-server architecture** with contract-sharing through a common library.

### 1.2 Responsibility of each project

- **AccountingSystem.Client**
  - UI pages/components, route protection, role-aware navigation.
  - Client-side auth state and JWT token storage.
  - Calls API endpoints through `ApiService` and domain services.
- **AccountingSystem.Api**
  - Authentication, authorization, tenant isolation, business workflows (GL/AP/AR/Reports/SuperAdmin).
  - EF Core persistence and migrations.
  - Cross-cutting middleware: JWT parsing, tenant access enforcement, audit logging.
- **AccountingSystem.Shared**
  - DTOs used in request/response payloads.
  - Enums and JSON serialization settings for contract consistency.

### 1.3 Dependency direction

- `AccountingSystem.Client -> AccountingSystem.Shared`
- `AccountingSystem.Api -> AccountingSystem.Shared`
- `AccountingSystem.Api` and `AccountingSystem.Client` do **not** directly reference each other.

### 1.4 Request flow (Client → API → Database)

1. Blazor page triggers a domain service (e.g., `ReceivableService`).
2. Domain service uses `ApiService` to call `/api/...` endpoint.
3. `ApiService` attaches Bearer token from local storage.
4. API pipeline validates JWT and enriches `HttpContext.Items` (`UserId`, `Role`, `CompanyId`).
5. Tenant-access middleware blocks blocked/suspended users/companies.
6. Controller delegates to service layer / DbContext.
7. EF Core query filters scope tenant data by `CompanyId` (except explicit `IgnoreQueryFilters()`).
8. API returns DTO/json or file response to client.

### 1.5 Authentication mechanism

- **JWT Bearer authentication** configured in API.
- Claims include: `UserId`, `role`, `CompanyId`, `CompanyName`, plus standard name/role claims.
- Client stores token in local storage (`authToken`) and rebuilds auth state from token claims.
- Route authorization is applied both:
  - server-side via `[Authorize]`/`[Authorize(Roles=...)]`
  - client-side via `AuthorizeRouteView`, page-level `[Authorize]`, and `AuthorizeView`.

### 1.6 State management strategy (Blazor)

- Authentication state: custom `AuthenticationStateProvider` (`CustomAuthStateProvider`).
- Session/token state: `TokenStorageService` + local storage.
- Feature data state: page-local component state (`List<T>`, filters, dialogs, loading flags) fetched via scoped services.
- No centralized Flux/Redux-style state store; state is primarily per-page and service-driven.

---

## 2. Backend Documentation

### 2.1 API composition

- **Controllers** expose REST endpoints and handle role-gated access.
- **Services** contain business logic (Auth, Ledger, Payables, Receivables, Payments, PDF, Tenant).
- **Persistence** is EF Core via `AccountingDbContext`.
- **Middleware** adds JWT claim extraction, tenant status enforcement, and audit logging.
- **Migrations** are present and actively used (`Database.Migrate()` at startup).

### 2.2 Program.cs and DI setup

- Registers SQL Server DbContext with `DefaultConnection`.
- Registers scoped services/interfaces:
  - `IAuthService`, `ILedgerService`, `IPayableService`, `IReceivableService`, `IPaymentService`, `IPdfService`, `ITenantService`.
- Registers captcha service using `HttpClient`.
- Configures JWT bearer validation (issuer, audience, symmetric signing key, zero clock skew).
- Configures CORS policy `AllowBlazorClient` for local client origins.
- Enables Swagger with Bearer security definition.
- Startup migration + seed execution via `DataSeeder.SeedDataAsync(context)`.
- Middleware order:
  - `UseAuthentication()`
  - `JwtMiddleware`
  - `TenantAccessMiddleware`
  - `UseAuthorization()`
  - `AuditMiddleware`

### 2.3 Middleware

- **JwtMiddleware**: parses bearer token and stores user metadata in `HttpContext.Items`.
- **TenantAccessMiddleware**: blocks non-superadmin calls when user/company status is blocked/suspended.
- **AuditMiddleware**: logs successful state-changing requests (POST/PUT/DELETE) into `AuditLogs` with action naming conventions.

### 2.4 Filters

- No custom ASP.NET MVC filter classes are present.
- Cross-cutting behavior is implemented primarily through middleware and EF query filters.

### 2.5 Authentication/authorization configuration

- Authentication: JWT Bearer.
- Authorization model: role-based attributes (`Admin`, `Accounting`, `Management`, `SuperAdmin`) plus authenticated-only routes.

### 2.6 DbContext

- `DbSet`s: `Companies`, `Users`, `Roles`, `Accounts`, `JournalEntries`, `JournalEntryLines`, `Vendors`, `Customers`, `Bills`, `Invoices`, `Payments`, `AuditLogs`, `SuperAdminAuditLogs`.
- Global query filters enforce tenant isolation and soft-delete visibility.
- Enum-to-string conversion for `DocumentStatus`, `PaymentMethod`, `PaymentType`.
- Decimal precision standardization (`18,2`).
- Constraints:
  - unique `User.Email`
  - unique `Account(Code, CompanyId)`
- Role seed data includes `SuperAdmin` role.
- `SaveChangesAsync` auto-manages timestamps, soft-delete behavior, and tenant assignment for `BaseEntity`.

### 2.7 Controller endpoint catalog

### Controller: AuthController

Base Route: `/api/auth`  
Authorization Required: Mixed (public + authenticated)

Endpoints:

- **[POST] /api/auth/login**
  - Description: User login and JWT issuance.
  - Request: `LoginDTO`.
  - Response: `AuthResponseDTO`.
  - Status Codes: `200`, `401`.
- **[POST] /api/auth/login/mfa**
  - Description: Completes second-step MFA login and issues JWT.
  - Request: `LoginMfaDTO`.
  - Response: `AuthResponseDTO`.
  - Status Codes: `200`, `401`, `400`.
- **[POST] /api/auth/login/mfa/email/send**
  - Description: Sends an Email OTP for an active MFA login challenge when Email OTP MFA is enabled.
  - Request: `SendLoginEmailOtpDTO`.
  - Status Codes: `200`, `400`, `401`, `429`.
- **[POST] /api/auth/register-company**
  - Description: Creates tenant company + admin user and sends email confirmation workflow.
  - Request: `CompanyRegisterDTO` (includes recaptcha token).
  - Response: `AuthResponseDTO`.
  - Status Codes: `200`, `400`.
- **[POST] /api/auth/forgot-password**
  - Description: Starts password reset workflow.
  - Request: `ForgotPasswordDTO`.
  - Response: message object.
  - Status Codes: `200`.
- **[POST] /api/auth/reset-password**
  - Description: Completes password reset with token.
  - Request: `ResetPasswordDTO`.
  - Response: message object.
  - Status Codes: `200`, `400`.
- **[POST] /api/auth/confirm-email**
  - Description: Confirms user email address.
  - Request: `ConfirmEmailDTO`.
  - Response: message object.
  - Status Codes: `200`, `400`.
- **[POST] /api/auth/resend-confirmation**
  - Description: Resends email confirmation link.
  - Request: `ResendConfirmationDTO`.
  - Response: message object.
  - Status Codes: `200`.
- **[GET] /api/auth/profile**
  - Description: Gets current user profile.
  - Response: profile DTO.
  - Status Codes: `200`, `401`.
- **[PUT] /api/auth/profile**
  - Description: Updates current user profile.
  - Request: `UpdateProfileDTO`.
  - Response: message object.
  - Status Codes: `200`, `400`, `401`.
- **[PUT] /api/auth/password**
  - Description: Changes current user password.
  - Request: `ChangePasswordDTO`.
  - Response: message object.
  - Status Codes: `200`, `400`, `401`.
- **[GET] /api/auth/mfa**
  - Description: Gets MFA status for current user.
  - Status Codes: `200`, `400`, `401`.
- **[POST] /api/auth/mfa/authenticator/setup**
  - Description: Begins authenticator setup and returns key/URI.
  - Status Codes: `200`, `400`, `401`.
- **[POST] /api/auth/mfa/authenticator/reset**
  - Description: Resets authenticator key after re-authentication.
  - Request: `MfaReauthenticationDTO`.
  - Status Codes: `200`, `400`, `401`.
- **[POST] /api/auth/mfa/authenticator/verify**
  - Description: Verifies authenticator setup code and enables MFA.
  - Request: `VerifyAuthenticatorSetupDTO`.
  - Status Codes: `200`, `400`, `401`.
- **[POST] /api/auth/mfa/recovery-codes/regenerate**
  - Description: Regenerates MFA recovery codes.
  - Request: `MfaReauthenticationDTO`.
  - Status Codes: `200`, `400`, `401`.
- **[POST] /api/auth/mfa/disable**
  - Description: Disables Authenticator App MFA for current user.
  - Request: `MfaReauthenticationDTO`.
  - Status Codes: `200`, `400`, `401`.
- **[POST] /api/auth/mfa/email/setup**
  - Description: Sends a setup verification code to the current user's confirmed email address.
  - Status Codes: `200`, `400`, `401`.
- **[POST] /api/auth/mfa/email/verify**
  - Description: Verifies setup code and enables Email OTP MFA.
  - Request: `VerifyEmailOtpMfaDTO`.
  - Status Codes: `200`, `400`, `401`.
- **[POST] /api/auth/mfa/email/disable**
  - Description: Disables Email OTP MFA after re-authentication.
  - Request: `MfaReauthenticationDTO`.
  - Status Codes: `200`, `400`, `401`.

### Controller: UsersController

Base Route: `/api/users`  
Authorization Required: Yes (`Admin`)

Endpoints:

- **[GET] /api/users?includeArchived={bool}**
- **[POST] /api/users**
- **[DELETE] /api/users/{id}** (soft archive)
- **[PUT] /api/users/{id}/restore**

### Controller: CompaniesController

Base Route: `/api/companies`  
Authorization Required: Yes (`[Authorize]`, update requires `Admin`)

Endpoints:

- **[GET] /api/companies/current**
- **[PUT] /api/companies/current**

### Controller: GeneralLedgerController

Base Route: `/api/ledger`  
Authorization Required: Per endpoint role

Endpoints:

- **[GET] /api/ledger/accounts?includeArchived={bool}** (`Admin,Accounting,Management`)
- **[POST] /api/ledger/accounts** (`Admin,Accounting`)
- **[PUT] /api/ledger/accounts/{id}** (`Admin,Accounting`)
- **[DELETE] /api/ledger/accounts/{id}** (`Admin,Accounting`)
- **[PUT] /api/ledger/accounts/{id}/restore** (`Admin,Accounting`)
- **[GET] /api/ledger/trial-balance** (`Admin,Accounting,Management`)
- **[POST] /api/ledger/journal** (`Admin,Accounting`)

### Controller: AccountsPayableController

Base Route: `/api/payables`  
Authorization Required: Yes (`Admin,Accounting`)

Endpoints:

- **[GET] /api/payables/vendors?includeArchived={bool}**
- **[POST] /api/payables/vendors**
- **[PUT] /api/payables/vendors/{id}**
- **[DELETE] /api/payables/vendors/{id}**
- **[PUT] /api/payables/vendors/{id}/restore**
- **[GET] /api/payables/bills**
- **[POST] /api/payables/bill**
- **[POST] /api/payables/bill/{id}/pay**

### Controller: AccountsReceivableController

Base Route: `/api/receivables`  
Authorization Required: Yes (`Admin,Accounting`)

Endpoints:

- **[GET] /api/receivables/customers?includeArchived={bool}**
- **[POST] /api/receivables/customers**
- **[PUT] /api/receivables/customers/{id}**
- **[DELETE] /api/receivables/customers/{id}**
- **[PUT] /api/receivables/customers/{id}/restore**
- **[GET] /api/receivables/invoices**
- **[POST] /api/receivables/invoice**
- **[POST] /api/receivables/invoice/{id}/receive**

### Controller: ReportsController

Base Route: `/api/reports`  
Authorization Required: Yes (authenticated)

Endpoints:

- **[GET] /api/reports/invoices/{id}/pdf**
  - Returns invoice PDF file.
- **[GET] /api/reports/financials/pdf**
  - Returns financial report PDF file.

### Controller: AuditLogsController

Base Route: `/api/audit-logs`  
Authorization Required: Yes (`Admin`)

Endpoints:

- **[GET] /api/audit-logs** (returns latest 500 logs)

### Controller: PaymentController

Base Route: `/api/payments`  
Authorization Required: Mixed

Endpoints:

- **[POST] /api/payments/paymongo-source** (`Admin,Accounting`)
  - Creates PayMongo payment source and returns checkout link/source id.
- **[POST] /api/payments/webhook** (`AllowAnonymous`)
  - Receives PayMongo webhook payload and validates the PayMongo HMAC signature/replay window before accepting it.

### Controller: SuperAdminController

Base Route: `/api/superadmin`  
Authorization Required: Yes (`SuperAdmin`)

Endpoints:

- **[GET] /api/superadmin/dashboard**
- **[GET] /api/superadmin/companies**
- **[PUT] /api/superadmin/companies/{id}/status**
- **[PUT] /api/superadmin/companies/{id}/toggle**
- **[GET] /api/superadmin/users**
- **[PUT] /api/superadmin/users/{id}/status**
- **[PUT] /api/superadmin/users/{id}/toggle**
- **[GET] /api/superadmin/audit-logs**

---

## 3. Shared Models & DTO Documentation

### 3.1 DTO inventory

| Domain        | DTOs                                                                                                                                   |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| Auth          | `LoginDTO`, `RegisterDTO`, `CompanyRegisterDTO`, `AuthResponseDTO`, `UpdateProfileDTO`, `ChangePasswordDTO`                            |
| Company       | `CompanyDTO`, `UpdateCompanyDTO`                                                                                                       |
| Users         | `UserDTO`, `GlobalUserDTO`, `UpdateUserStatusDTO`                                                                                      |
| Ledger        | `AccountDTO`, `CreateAccountDTO`, `UpdateAccountDTO`, `JournalEntryDTO`, `JournalEntryLineDTO`, `TrialBalanceDTO`, `AccountBalanceDTO` |
| AP            | `VendorDTO`, `CreateVendorDTO`, `UpdateVendorDTO`, `BillDTO`, `CreateBillDTO`                                                          |
| AR            | `CustomerDTO`, `CreateCustomerDTO`, `UpdateCustomerDTO`, `InvoiceDTO`, `CreateInvoiceDTO`                                              |
| Payments      | `RecordPaymentDTO`, `PaymentHistoryDTO`, `CreateSourceDTO`, `PaymentSourceResponseDTO`, PayMongo request/response/webhook DTOs         |
| Audit         | `AuditLogDTO`, `SuperAdminAuditLogDTO`, `SystemDashboardDTO`, `MonthlyActivityDTO`, `TenantDTO`, `UpdateCompanyStatusDTO`              |
| External data | `WorldBankDataPoint`, `WorldBankIndicator`, `FrankfurterResponse`, `FrankfurterRates`                                                  |

### 3.2 Mapping relationships (DTO ↔ Entity)

- `AccountDTO` ↔ `Account`
- `VendorDTO` ↔ `Vendor`
- `CustomerDTO` ↔ `Customer`
- `BillDTO` ↔ `Bill`
- `InvoiceDTO` ↔ `Invoice`
- `CompanyDTO` ↔ `Company`
- `UserDTO` ↔ `User (+Role.Name)`
- `AuditLogDTO` ↔ `AuditLog` + user email join
- `SuperAdminAuditLogDTO` ↔ `SuperAdminAuditLog`
- `JournalEntryDTO`/`JournalEntryLineDTO` ↔ `JournalEntry`/`JournalEntryLine`

Mapping is done manually inside controllers/services (no AutoMapper).

### 3.3 Validation attributes

Used heavily in DTOs, e.g.:

- `[Required]`, `[EmailAddress]`, `[StringLength]`, `[MinLength]`, `[Compare]`, `[MaxLength]`.
- Validation exists in DTO contracts; business rules are additionally enforced in services (e.g., overpayment checks, balanced journal entries).

### 3.4 Serialization behavior

- Enums use `JsonStringEnumConverter` in shared enums and payment DTO fields.
- External API models rely on `[JsonPropertyName]` to align with provider payloads (PayMongo, World Bank, Frankfurter).
- API entities hide sensitive fields (`PasswordHash`, `PasswordSalt`) with `[JsonIgnore]`.

---

## 4. Frontend Documentation

### 4.1 Frontend architecture summary

- Blazor WASM app with MudBlazor UI.
- Routing via `App.razor` + `AuthorizeRouteView`.
- Role-based menu rendering in `NavMenu.razor`.
- Feature services wrap API routes and are consumed by pages.

### 4.2 Pages/features

### Feature: Login (`/`)

Purpose: Authenticate user and establish session.  
Connected API Endpoints: `POST /api/auth/login`.  
Data Models Used: `LoginDTO`, `AuthResponseDTO`.  
User Flow: submit credentials → API returns JWT or MFA challenge/email-confirmation requirement → token stored only after successful completion → navigate authorized dashboard.

### Feature: Register Company (`/register`)

Purpose: Tenant self-onboarding + admin account creation.  
Connected API Endpoints: `POST /api/auth/register-company`.  
Data Models Used: `CompanyRegisterDTO`, `AuthResponseDTO`.  
User Flow: fill company/admin form + recaptcha → API creates tenant + admin account → confirmation email workflow is triggered before normal sign-in.

### Feature: Dashboard (`/dashboard`)

Purpose: Main financial summary view with key metrics/charts.  
Connected API Endpoints: trial balance, ledger/AP/AR aggregations, external data service calls from client.  
Data Models Used: `TrialBalanceDTO`, account/bill/invoice DTOs.  
User Flow: authorized user opens dashboard → data fetched from multiple services.

### Feature: User Profile (`/profile`)

Purpose: Manage own profile, password, Authenticator App MFA, Email OTP MFA, and recovery codes.  
Connected API Endpoints: `PUT /api/auth/profile`, `PUT /api/auth/password`, `/api/auth/mfa/*`.  
Data Models Used: `UpdateProfileDTO`, `ChangePasswordDTO`, MFA DTOs.  
User Flow: edit profile/password or manage authenticator/recovery codes → save → snackbar feedback.

### Feature: General Ledger Accounts (`/gl/accounts`)

Purpose: Manage chart of accounts (includes archive/restore).  
Connected API Endpoints: `/api/ledger/accounts*`.  
Data Models Used: `AccountDTO`, `CreateAccountDTO`, `UpdateAccountDTO`.  
User Flow: list/filter accounts → create/update/archive/restore by role.

### Feature: Journal Entries (`/gl/journal`)

Purpose: Create double-entry journal entries.  
Connected API Endpoints: `POST /api/ledger/journal`.  
Data Models Used: `JournalEntryDTO`, `JournalEntryLineDTO`.  
User Flow: build debit/credit lines → post entry → validation/error handling.

### Feature: Vendors (`/ap/vendors`)

Purpose: Manage AP vendors.  
Connected API Endpoints: `/api/payables/vendors*`.  
Data Models Used: `VendorDTO`, `CreateVendorDTO`, `UpdateVendorDTO`.  
User Flow: create/edit/archive/restore vendor records.

### Feature: Bills (`/ap/bills`) and Bill List (`/ap/bills/list`)

Purpose: Create and track vendor bills; record outgoing payments.  
Connected API Endpoints: `/api/payables/bill`, `/api/payables/bills`, `/api/payables/bill/{id}/pay`.  
Data Models Used: `CreateBillDTO`, `BillDTO`, `RecordPaymentDTO`.  
User Flow: create bill → auto ledger posting in API → view bills → pay bill.

### Feature: Customers (`/ar/customers`)

Purpose: Manage AR customers.  
Connected API Endpoints: `/api/receivables/customers*`.  
Data Models Used: `CustomerDTO`, `CreateCustomerDTO`, `UpdateCustomerDTO`.  
User Flow: create/edit/archive/restore customer data.

### Feature: Invoices (`/ar/invoices`) and Invoice List (`/ar/invoices/list`)

Purpose: Create and monitor customer invoices; export invoice PDF.  
Connected API Endpoints: `/api/receivables/invoice`, `/api/receivables/invoices`, `/api/reports/invoices/{id}/pdf`.  
Data Models Used: `CreateInvoiceDTO`, `InvoiceDTO`.  
User Flow: create invoice → ledger impact in API → list/filter/download.

### Feature: Receive Payment (`/ar/receive-payment`) + Payment Callback (`/payment-callback`)

Purpose: Handle PayMongo-based receivable payments (test mode for local/academic demonstration).  
Connected API Endpoints: `POST /api/payments/paymongo-source`, `POST /api/receivables/invoice/{id}/receive`.  
Data Models Used: `CreateSourceDTO`, `PaymentSourceResponseDTO`, `RecordPaymentDTO`.  
User Flow: generate checkout URL → redirect to provider checkout → callback page returns user to app flow.

### Feature: Financial Reports (`/reports/financials`)

Purpose: View trial balance context and export financial PDF reports.  
Connected API Endpoints: `GET /api/ledger/trial-balance`, `GET /api/reports/financials/pdf`.  
Data Models Used: `TrialBalanceDTO`, `CompanyDTO`.  
User Flow: open report page → fetch data → download generated PDF.

### Feature: Admin - User Management (`/admin/users`)

Purpose: Tenant admin user lifecycle management.  
Connected API Endpoints: `/api/users*`.  
Data Models Used: `UserDTO`, `RegisterDTO`.  
User Flow: view active/archived users → create, archive, restore.

### Feature: Admin - Audit Logs (`/admin/audit-logs`)

Purpose: View tenant audit trail.  
Connected API Endpoints: `GET /api/audit-logs`.  
Data Models Used: `AuditLogDTO`.  
User Flow: query logs and inspect action history.

### Feature: Admin - Company Settings (`/admin/company-settings`)

Purpose: Update tenant profile info.  
Connected API Endpoints: `GET/PUT /api/companies/current`.  
Data Models Used: `CompanyDTO`, `UpdateCompanyDTO`.  
User Flow: load current company → edit settings → save.

### Feature: SuperAdmin pages (`/superadmin/*`)

Purpose: Multi-tenant governance and platform monitoring.

- `SystemDashboard`: platform KPIs/trends/recent actions.
- `TenantManager`: list tenants + status management.
- `GlobalUserManager`: global user status management.
- `AdminAuditLogs`: superadmin action history.
  Connected API Endpoints: `/api/superadmin/*`.  
  Data Models Used: `SystemDashboardDTO`, `TenantDTO`, `GlobalUserDTO`, `SuperAdminAuditLogDTO`, status update DTOs.

---

## 5. Database & Data Flow

### 5.1 DbContext structure

Main entities: company/user/role, ledger accounts/journal entries, AP (vendors/bills), AR (customers/invoices), payments, tenant and superadmin audit logs.

### 5.2 Entity relationships

- `Company` 1..\* `User`, `Account`, `Vendor`, `Customer`, `Bill`, `Invoice`, `Payment` (via `CompanyId` on `BaseEntity`).
- `Role` 1..\* `User`.
- `JournalEntry` 1..\* `JournalEntryLine`.
- `JournalEntryLine` \*..1 `Account`.
- `Vendor` 1..\* `Bill`.
- `Customer` 1..\* `Invoice`.
- `Payment` optionally references `Invoice`, `Bill`, and `Account`.

### 5.3 Migrations

Migrations folder indicates active schema evolution (multi-tenancy, audit tenancy fixes, superadmin enhancements, etc.).

### 5.4 Data lifecycle

- Create/update operations set timestamps and tenant context automatically in `SaveChangesAsync`.
- Delete operations are soft-deletes for most `BaseEntity` entities (`IsDeleted=true`, `IsActive=false`).
- Read operations are tenant-scoped and soft-delete filtered unless explicitly bypassed.

### 5.5 Typical transaction flow

**Example: Create Invoice**

1. Client posts `CreateInvoiceDTO`.
2. API `AccountsReceivableController` calls `IReceivableService.CreateInvoiceAsync`.
3. Service creates invoice and corresponding journal entry lines.
4. DbContext saves invoice + ledger impacts.
5. Trial balance reflects changes.

**Example: Pay Bill**

1. Client posts `RecordPaymentDTO`.
2. Service validates amount (no overpayment).
3. Bill status/amount paid updated.
4. Payment record + journal entries are created.
5. Commit and return payment result.

---

## 6. Cross-Project Communication

### 6.1 How Shared is referenced

Both API and Client include project references to `AccountingSystem.Shared`, creating a unified contract model.

### 6.2 DTO movement between layers

- Client pages build DTOs → client services post/get to API.
- API controllers accept DTOs, call services.
- Services map DTOs to EF entities and back to DTOs.
- Response DTOs are rendered by client pages/components.

### 6.3 Model mapping strategy

- Manual in-code mapping inside services/controllers using LINQ projections and object initializers.
- This keeps mapping explicit but can become repetitive as models grow.

### 6.4 Dependency flow rules

- UI should not reference API internals.
- API should keep EF entities internal to server side; external contracts remain shared DTOs.
- Shared project should remain transport-contract focused (no infrastructure dependencies).

---

## 7. Technology Stack

- **.NET Version:** .NET 8 (`net8.0` for all projects)
- **Backend:** ASP.NET Core 8 Web API
- **Frontend:** Blazor WebAssembly (WASM)
- **Database:** Microsoft SQL Server accessed through EF Core 8
- **Authentication:** JWT bearer tokens with role-based authorization
- **Email Delivery:** SMTP via `SmtpClient` (Gmail App Password compatible when Gmail SMTP is used)
- **Bot Protection:** Google reCAPTCHA v2 Checkbox in company registration flow and always-on login reCAPTCHA required for every non-locked login attempt
- **Payment Integration:** PayMongo source and redirect flow (test-mode usage for local/academic demonstration)
- **Additional libraries:** MudBlazor, Blazored.LocalStorage, QuestPDF, Swashbuckle

---

## 8. Security Overview

### 8.1 Password Policy

Password policy is enforced through `AccountingSystem.Shared/Validation/PasswordPolicy.cs`:

- Complex password option: minimum 12 characters and at least 3 of 4 character classes (uppercase, lowercase, digit, symbol)
- Passphrase option: minimum 16 characters with at least 3 words
- Maximum length: 128 characters

### 8.2 Login Attempt and Lockout Policy

Default lockout settings in configuration:

- `AuthSecurity:Lockout:MaxFailedAccessAttempts = 5`
- `AuthSecurity:Lockout:LockoutMinutes = 5`

The login UI intentionally does not show exact attempts left or a lockout countdown. This limits attacker feedback while still showing generic user-friendly errors for CAPTCHA and temporary lockout states.

Login reCAPTCHA is shown by default and required before credential processing. Account lockout still applies after the configured failed attempts.

Rate limiting is configured per auth endpoint (login, register-company, forgot/reset password, confirm/resend confirmation, MFA login, MFA management).

### 8.3 Authentication Features

Implemented features:

- Login with JWT issuance (`POST /api/auth/login`)
- Forgot password (`POST /api/auth/forgot-password`)
- Reset password (`POST /api/auth/reset-password`)
- Email confirmation (`POST /api/auth/confirm-email`)
- Resend confirmation (`POST /api/auth/resend-confirmation`)
- Optional MFA:
  - Authenticator App MFA with recovery codes
  - Email OTP MFA to a confirmed email address
  - Authenticator App MFA and Email OTP MFA are independently managed from the user profile
- Registration protected by Google reCAPTCHA token verification
- Login protected by Google reCAPTCHA token verification on every normal login attempt

Password handling status:

- ASP.NET Core Identity password hashing is used for provisioned Identity accounts
- Legacy hash fields and legacy verification path remain for compatibility with legacy user data

### 8.4 Authorization, RBAC, and Tenant Isolation

- Roles present in seed and authorization attributes: `Admin`, `Accounting`, `Management`, `SuperAdmin`
- API role enforcement through `[Authorize]` and `[Authorize(Roles = ...)]`
- Client route/page enforcement through `AuthorizeRouteView`, page attributes, and `AuthorizeView`
- Tenant isolation implemented through middleware checks and EF Core query filters by `CompanyId`

### 8.5 Data Handling Policy

- Passwords are not stored in plaintext
- Sensitive runtime values are expected from `.env` or environment variables
- `appsettings.json` contains placeholder values (`__SET_VIA_ENV__`) for sensitive keys
- HTTPS redirection is enabled in API startup
- Database operations are performed through EF Core and tenant-scoped data access rules

### 8.6 Logging and Monitoring Policy

- `AuditMiddleware` records successful state-changing non-auth requests
- Auth and account-security events are captured by `AuthSecurityAuditService`
- Tenant audit logs display System and Security categories
- Super-admin actions are recorded in `SuperAdminAuditLogs`
- SuperAdmin-account login failures, lockouts, CAPTCHA-required events, MFA challenges, and successful logins are mirrored into SuperAdmin governance logs.
- OTP values, recovery codes, CAPTCHA tokens, passwords, JWTs, and secrets are not written to audit details.
- Local development can use a logging email sender when SMTP is not configured

### 8.7 Incident Response Plan

1. **Detection**: detect abnormal behavior using application logs, audit logs, and auth-security events.
2. **Reporting**: document the incident scope (affected endpoint, tenant, time window, actor).
3. **Containment**: temporarily disable or restrict affected flows, rotate secrets, and block compromised accounts.
4. **Recovery**: restore validated configuration, re-test affected security controls, and resume service.
5. **Review**: record root cause, corrective actions, and evidence for academic and operational review.

### 8.8 Access Control Matrix

| Actor | Authentication | Example Access | Restricted Access |
| --- | --- | --- | --- |
| Guest | None | Login, register-company, forgot/reset password, confirm/resend confirmation | Tenant and super-admin protected pages/endpoints |
| Admin | JWT | Tenant user management, company settings, GL/AP/AR operations, audit logs | Super-admin endpoints |
| Accounting | JWT | GL/AP/AR transaction workflows, payment source flow, reports | Tenant user management, super-admin endpoints |
| Management | JWT | Dashboard and selected reporting/ledger read operations | User management, most transaction-write endpoints, super-admin endpoints |
| SuperAdmin | JWT | Cross-tenant governance endpoints and super-admin audit logs | Tenant-scoped admin actions not exposed to super-admin role |

### 8.9 Code Auditing and Tools

Current in-repo evidence:

- Unit tests for auth and account flows (`AccountingSystem.API.Tests`)
- Runtime audit logging for business and auth events
- GitHub Actions workflow `.github/workflows/security-tooling-evidence.yml`:
  - restore/build checks for `AccountingSystem.sln`
  - API and client test execution
  - dependency vulnerability report generation (`dotnet list package --vulnerable --include-transitive`) with artifact evidence
  - gitleaks secret-scan execution in report-first/non-blocking mode
- GitHub Actions workflow `.github/workflows/codeql.yml`:
  - CodeQL analysis for C# on push, pull request, and weekly schedule

Remaining improvements:

- Tighten CI policy gates after baseline triage (for example, fail thresholds for high/critical issues).
- Add formatting/style verification gates when project-wide warning baseline is stable.

### 8.10 Evidence Checklist

- [x] Login flow and JWT issuance
- [x] Forgot password request flow
- [x] Reset password flow
- [x] Email confirmation and resend confirmation flows
- [x] reCAPTCHA-protected registration
- [x] Always-on login reCAPTCHA
- [x] TOTP Authenticator App MFA
- [x] Email OTP MFA
- [x] PayMongo source/redirect payment flow (test mode)
- [x] Protected dashboard and role-gated pages
- [x] Audit logs and auth-security audit events

### 8.11 Known Limitations and Recommended Improvements

- **Known Limitation:** Email OTP challenges are stored in memory for this demo build; pending codes are lost if the API restarts.
- **Known Limitation:** Client-side JWT storage in local storage increases risk exposure if XSS is introduced.
- **Recommended Improvement:** Move CI security tooling from evidence-first reporting to stricter enforcement gates after remediation baseline.
- **Recommended Improvement:** Add refresh token and server-side revocation strategy.
- **Recommended Improvement:** Use database or distributed-cache backed Email OTP challenge storage for production or multi-instance deployments.

---

## 9. Setup & Deployment Guide

### 9.1 Local run steps

```bash
# from repository root
dotnet restore AccountingSystem.sln

# configure required secrets first
# see SECURITY_CONFIGURATION.md

# API
cd AccountingSystem.Api
dotnet run

# Client (new terminal)
cd ../AccountingSystem.Client
dotnet run
```

For a full local reset in a two-`DbContext` solution, use the explicit context workflow:

```powershell
# Preferred workflow from the repo root
.\scripts\reset-dev-db.ps1

# Equivalent Package Manager Console command
Drop-Database -Context AccountingDbContext

# Equivalent CLI command
dotnet ef database drop --context AccountingDbContext --project AccountingSystem.Api/AccountingSystem.Api.csproj --startup-project AccountingSystem.Api/AccountingSystem.Api.csproj --force
```

After the database is dropped, start the API again. API startup migrates `AccountingDbContext`, then `IdentityAuthDbContext`, then runs `DataSeeder`. `Update-Database` alone does not execute the seeder.

### 9.2 Required configuration/environment

API requires configuration values for:

- `ConnectionStrings:DefaultConnection`
- `JwtSettings:Secret`, `Issuer`, `Audience`, `ExpiryMinutes`
- `PayMongo:SecretKey` / `PublicKey`
- `Recaptcha:SecretKey`, `ScoreThreshold`
- SMTP settings and `AppUrls:ClientBaseUrl` for password-reset delivery
- `BootstrapAdmin:*` on the first API run when the database has no super-admin yet

### 9.3 Configuration files

- `AccountingSystem.Api/appsettings.json` (checked in with placeholder values for sensitive keys)
- `AccountingSystem.Api/.env.example` (sample key list for local setup)
- local `.env` file (developer machine only; should not be committed)
- environment variables / deployment secret store

Notes:

- No additional environment-specific or template appsettings files are currently tracked in this repository.
- Project files currently rely on `.env` and environment-variable configuration rather than project-scoped user-secrets metadata.

See `SECURITY_CONFIGURATION.md` for the required key list and runtime secret policy.

### 9.4 Build commands

```bash
dotnet build AccountingSystem.sln
```

### 9.5 Production considerations

- Move all secrets to secure secret stores (Key Vault/env vars).
- Restrict CORS origins to production domains.
- Enable strict HTTPS and secure reverse-proxy settings.
- Keep PayMongo webhook signature secrets aligned with deployed webhook configuration.
- Consider CI/CD migration strategy and zero-downtime deployment plan.

---

## 10. Recommendations & Improvements

### 10.1 Architectural improvements

- Introduce repository/specification pattern only where query complexity justifies it.
- Add application layer abstractions for clearer use-case boundaries.
- Consider splitting superadmin module into bounded context.

### 10.2 Refactoring suggestions

- Consolidate repeated CRUD patterns in AP/AR/GL services.
- Normalize response envelope/error model for consistent client handling.
- Add mapping utilities (or AutoMapper) to reduce manual mapping duplication.

### 10.3 Security improvements

- Use database or distributed-cache backed Email OTP challenge storage for production or multi-instance deployments.
- Continue normalizing auth error responses to consistent sanitized error contracts.
- Evaluate migration away from browser local storage for token persistence or strengthen compensating controls.
- Introduce CI-based security checks (SAST, dependency vulnerability scanning, and secrets scanning).
- Design refresh-token and revocation controls for stronger session lifecycle management.

### 10.4 Performance considerations

- Add pagination for heavy list endpoints (users, logs, invoices, bills).
- Add caching for static/reference datasets (chart of accounts, company profile).
- Review eager-loading patterns and index strategy for large tenants.

### 10.5 Scalability considerations

- Introduce structured logging + centralized observability.
- Consider background jobs for heavy report generation.
- Prepare horizontal scaling strategy (stateless API, distributed cache, queue-based integration).

---

## Appendix A — API example payloads

### Login request

```json
{
  "email": "admin@company.com",
  "password": "your-password",
  "recaptchaToken": "client-recaptcha-response-token"
}
```

### Create journal entry

```json
{
  "description": "Office supplies purchase",
  "reference": "JV-2026-001",
  "date": "2026-02-01T00:00:00Z",
  "lines": [
    { "accountId": 15, "debit": 1000, "credit": 0 },
    { "accountId": 2, "debit": 0, "credit": 1000 }
  ]
}
```

### Record receivable payment

```json
{
  "referenceId": 12,
  "amount": 2500,
  "paymentDate": "2026-02-10T09:00:00Z",
  "paymentMethod": "Online",
  "referenceNumber": "PM-ABC123",
  "assetAccountId": 1,
  "remarks": "Partial payment"
}
```


to run in powershell:
cd C:\Projects\AccountingSystem\AccountingSystem.Api
dotnet run

in another powershell:
cd C:\Projects\AccountingSystem\AccountingSystem.Client
dotnet run
