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
- hardened webhook signature verification
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

## Known Limitations and Recommended Improvements

- **Known Limitation:** webhook signature verification logic for PayMongo is currently permissive and must be hardened.
- **Recommended Improvement:** add automated secret scanning and configuration policy checks in CI.
