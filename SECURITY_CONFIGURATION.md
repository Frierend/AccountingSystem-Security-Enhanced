# SECURITY_CONFIGURATION

## Purpose

This document defines the secure configuration policy for `AccountingSystem.Api`.

The project policy is:

- keep sensitive values out of source code
- keep sensitive values out of committed runtime configuration
- provide sensitive values through environment variables and/or local `.env` in development

## Secure Configuration Policy

- `AccountingSystem.Api/appsettings.json` is committed with placeholders for sensitive values (`__SET_VIA_ENV__`).
- Local development may use a non-committed `.env` file loaded by `DotNetEnv`.
- Production should use environment variables or a managed secret store.
- Secrets must never be committed to Git history.

## Required Configuration Keys

### Always required at startup

- `ConnectionStrings__DefaultConnection`
- `JwtSettings__Secret`
- `JwtSettings__Issuer`
- `JwtSettings__Audience`
- `JwtSettings__ExpiryMinutes`
- `JwtSettings__ClockSkewSeconds`
- `IdentityTokens__PasswordResetTokenLifespanMinutes`
- `IdentityTokens__EmailConfirmationTokenLifespanMinutes`
- `Mfa__AuthenticatorIssuer`
- `Mfa__LoginChallengeLifespanMinutes`
- `Mfa__EmailOtpExpirationMinutes`
- `Mfa__EmailOtpMaxVerificationAttempts`
- `Mfa__EmailOtpResendCooldownSeconds`
- `AuthSecurity__Lockout__MaxFailedAccessAttempts`
- `AuthSecurity__Lockout__LockoutMinutes`
- `AuthSecurity__RateLimiting__Login__PermitLimit`
- `AuthSecurity__RateLimiting__Login__WindowSeconds`
- `AuthSecurity__RateLimiting__RegisterCompany__PermitLimit`
- `AuthSecurity__RateLimiting__RegisterCompany__WindowSeconds`
- `AuthSecurity__RateLimiting__ChangePassword__PermitLimit`
- `AuthSecurity__RateLimiting__ChangePassword__WindowSeconds`
- `AuthSecurity__RateLimiting__ForgotPassword__PermitLimit`
- `AuthSecurity__RateLimiting__ForgotPassword__WindowSeconds`
- `AuthSecurity__RateLimiting__ResetPassword__PermitLimit`
- `AuthSecurity__RateLimiting__ResetPassword__WindowSeconds`
- `AuthSecurity__RateLimiting__ConfirmEmail__PermitLimit`
- `AuthSecurity__RateLimiting__ConfirmEmail__WindowSeconds`
- `AuthSecurity__RateLimiting__ResendConfirmation__PermitLimit`
- `AuthSecurity__RateLimiting__ResendConfirmation__WindowSeconds`
- `AuthSecurity__RateLimiting__LoginMfa__PermitLimit`
- `AuthSecurity__RateLimiting__LoginMfa__WindowSeconds`
- `AuthSecurity__RateLimiting__MfaManage__PermitLimit`
- `AuthSecurity__RateLimiting__MfaManage__WindowSeconds`
- `AppUrls__ClientBaseUrl`

### Required outside Development

- `PayMongo__SecretKey`
- `Recaptcha__SiteKey` (public; retained for the compatibility config endpoint)
- `Recaptcha__SecretKey`
- `Smtp__Host`
- `Smtp__Port`
- `Smtp__Username`
- `Smtp__Password`
- `Smtp__FromAddress`
- `Smtp__FromName`
- `Smtp__EnableSsl`

### Conditionally required

- `PayMongo__PublicKey` (documented for configuration completeness)
- `Recaptcha__ScoreThreshold`
- `BootstrapAdmin__Email`
- `BootstrapAdmin__FullName`
- `BootstrapAdmin__InitialPassword`

`BootstrapAdmin__*` values are required when creating the first super-admin account in an uninitialized environment.
The seeded/demo bootstrap SuperAdmin is created with `EmailConfirmed = true` so Email OTP MFA can be enabled during demonstrations. Backup SuperAdmins created from the UI use the normal email confirmation flow and start unconfirmed until the confirmation link is used.

## Security Control Notes

- Registration reCAPTCHA is implemented through Google reCAPTCHA v2 Checkbox.
- Login reCAPTCHA is always shown on the login page and required server-side for every non-locked login attempt.
- The reCAPTCHA public site key is client-side and may appear in the Blazor auth pages.
- `GET /api/auth/recaptcha/config` may remain available for compatibility, but login and registration do not depend on fetching the site key before rendering.
- `Recaptcha__SecretKey` is server-only and must not appear in client files or committed configuration.
- Account lockout still applies after the configured failed attempts, with a 5-minute demo/presentation lockout in the current configuration.
- Authenticator App MFA and Email OTP MFA are optional and independently managed from the user profile.
- Email OTP MFA requires confirmed email; the profile can resend confirmation before setup.
- Backup SuperAdmin creation is supported, and the last active SuperAdmin account cannot be disabled or deleted.
- Creating/enabling/disabling SuperAdmin accounts is a sensitive governance action and requires step-up verification (password re-entry, MFA when enabled, and reason/justification logging).
- SuperAdmin governance audit logs capture step-up verification outcomes and governance actions without storing passwords, MFA codes, OTPs, recovery codes, CAPTCHA tokens, JWTs, or secrets.
- SuperAdmin audit logs provide platform-level governance and security-event visibility for privileged account actions.

If a SuperAdmin account is suspected to be compromised, use a trusted backup SuperAdmin account to review governance logs, disable suspicious accounts, reset credentials, and rotate affected secrets when needed.

## Local Development Setup

### Recommended approach

1. Copy `AccountingSystem.Api/.env.example` to `AccountingSystem.Api/.env`.
2. Set all required keys for your local machine.
3. Do not commit the `.env` file.
4. Start API and confirm startup validation passes.

### Example key pattern (PowerShell session)

```powershell
$env:ConnectionStrings__DefaultConnection = "Data Source=YOUR_SERVER;Initial Catalog=AccountingSystemDB;Integrated Security=True;Trust Server Certificate=True"
$env:JwtSettings__Secret = "replace-with-long-random-secret"
$env:Smtp__Host = "smtp.gmail.com"
$env:Smtp__Port = "587"
$env:Smtp__Username = "your-email@gmail.com"
$env:Smtp__Password = "your-gmail-app-password"
$env:Smtp__EnableSsl = "true"
$env:Recaptcha__SiteKey = "your-public-recaptcha-site-key"
$env:Recaptcha__SecretKey = "your-private-recaptcha-secret-key"
```

### SMTP note (Gmail App Password)

When Gmail SMTP is used, the `Smtp__Password` value should be a Gmail App Password (not the account login password).

### Development fallback behavior

- If SMTP values are not configured in Development, the API can use `LoggingAccountEmailService` for local testing workflows.
- In this mode, reset and confirmation links are written to logs instead of being sent by email.

## Production Configuration Expectations

- Use environment variables or managed secret stores.
- Do not store live secrets in `appsettings.json`.
- Enforce secret rotation procedures for JWT, SMTP, PayMongo, and database credentials.
- Keep `AppUrls__ClientBaseUrl` aligned with deployed client URL for reset/confirmation links.

### PayMongo mode note

For this academic project, payment integration is documented and tested in **PayMongo test mode**. Production cutover requires:

- production keys
- continued webhook signature validation against the production webhook secret
- operational monitoring for payment callback/webhook paths

## Committed Configuration Policy

- `AccountingSystem.Api/appsettings.json` may contain non-sensitive defaults and placeholders only.
- `AccountingSystem.Api/.env.example` may contain key names and non-sensitive sample values only.
- Never commit:
  - database credentials
  - JWT signing secrets
  - SMTP credentials
  - reCAPTCHA secret keys
  - PayMongo secret keys
  - bootstrap admin initial passwords
  - OTP values, recovery codes, CAPTCHA tokens, or JWTs

## Known Limitations and Recommended Improvements

- **Known Limitation:** Email OTP challenges are stored in memory for this demo build; use database or distributed-cache backed storage for production or multi-instance deployments.
- **Recommended Improvement:** add automated secret scanning and configuration policy checks in CI.
