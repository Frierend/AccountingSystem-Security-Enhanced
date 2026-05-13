# Security Implementation Locator

## Purpose

This file lists the main security-related features of AccSys and where to find their implementation in the source code. It is intended to help locate and verify each implemented control during checking, documentation, or defense preparation.

## Implementation Map

| Security Feature | What It Does | Main Files / Locations | Evidence to Capture |
| --- | --- | --- | --- |
| Login Authentication | Validates user credentials and issues login result. | AccountingSystem.Api/Services/AuthService.cs; AccountingSystem.Api/Controllers/AuthController.cs; AccountingSystem.Client/Pages/Auth/Login.razor; AccountingSystem.Client/Services/AuthService.cs | Login page screenshot; successful login screenshot |
| Login reCAPTCHA | Requires "I'm not a robot" verification during login. | AccountingSystem.Client/Pages/Auth/Login.razor; AccountingSystem.Api/Services/CaptchaService.cs; AccountingSystem.Api/Controllers/AuthController.cs; AccountingSystem.Api/appsettings.json; AccountingSystem.Api/.env.example | Login page with reCAPTCHA; failed login without CAPTCHA |
| Registration reCAPTCHA | Requires reCAPTCHA verification during company registration. | AccountingSystem.Client/Pages/Auth/RegisterCompany.razor; AccountingSystem.Api/Services/CaptchaService.cs; AccountingSystem.Api/Controllers/AuthController.cs | Registration page reCAPTCHA |
| Account Lockout | Temporarily locks accounts after repeated failed login attempts. | AccountingSystem.Api/Services/AuthService.cs; AccountingSystem.Api/Security/AuthFailureException.cs; AccountingSystem.Client/Pages/Auth/Login.razor | Temporary lockout message screenshot |
| Rate Limiting | Applies request limits to sensitive authentication and SuperAdmin endpoints. | AccountingSystem.Api/Program.cs; AccountingSystem.Api/Configuration/AuthRateLimitPolicyNames.cs; AccountingSystem.Api/Controllers/AuthController.cs; AccountingSystem.Api/Controllers/SuperAdminController.cs | Too many requests message; code screenshot of policy usage |
| Password Policy and Hashing | Enforces password rules and hashes passwords through ASP.NET Core Identity. | AccountingSystem.Api/Program.cs; AccountingSystem.Api/Identity/ApplicationUser.cs; AccountingSystem.Api/Identity/SharedPasswordIdentityValidator.cs; AccountingSystem.Shared/Validation/PasswordPolicy.cs; AccountingSystem.Api/Services/AuthService.cs | Password validation screenshot; documentation screenshot |
| Forgot Password / Reset Password | Sends reset email flow and applies password reset through Identity services. | AccountingSystem.Client/Pages/Auth/ForgotPassword.razor; AccountingSystem.Client/Pages/Auth/ResetPassword.razor; AccountingSystem.Api/Controllers/AuthController.cs; AccountingSystem.Api/Services/AuthService.cs; AccountingSystem.Api/Services/SmtpAccountEmailService.cs; AccountingSystem.Api/Services/LoggingAccountEmailService.cs | Forgot password email/test screenshot |
| Email Confirmation | Supports email confirmation and resend confirmation flow. | AccountingSystem.Api/Controllers/AuthController.cs; AccountingSystem.Api/Services/AuthService.cs; AccountingSystem.Client/Pages/Auth/UserProfile.razor; AccountingSystem.Client/Pages/Auth/ConfirmEmail.razor; AccountingSystem.Client/Pages/Auth/ResendConfirmation.razor; AccountingSystem.Api/Services/SmtpAccountEmailService.cs; AccountingSystem.Api/Services/LoggingAccountEmailService.cs | Confirmation email screenshot; confirmed email/profile screenshot |
| Authenticator App MFA | Supports authenticator app setup, verification, login, and management. | AccountingSystem.Api/Services/MfaService.cs; AccountingSystem.Api/Controllers/AuthController.cs; AccountingSystem.Client/Pages/Auth/UserProfile.razor; AccountingSystem.Client/Pages/Auth/MfaLogin.razor | QR/manual key setup screenshot without exposing secret in final public copy; MFA login screenshot |
| Email OTP MFA | Sends and verifies email OTP codes for MFA setup and login. | AccountingSystem.Api/Services/MfaService.cs; AccountingSystem.Api/Services/EmailOtpChallengeStore.cs; AccountingSystem.Api/Controllers/AuthController.cs; AccountingSystem.Client/Pages/Auth/UserProfile.razor; AccountingSystem.Client/Pages/Auth/MfaLogin.razor | Email OTP enabled screenshot; OTP email screenshot with code blurred |
| Recovery Codes | Provides recovery-code option for MFA login and recovery-code management. | AccountingSystem.Api/Services/MfaService.cs; AccountingSystem.Client/Pages/Auth/MfaLogin.razor; AccountingSystem.Client/Pages/Auth/UserProfile.razor | Recovery code option screenshot with codes hidden/blurred |
| Tenant Audit Logs | Records company-level system and security activity for tenant review. | AccountingSystem.Client/Pages/Admin/AuditLogs.razor; AccountingSystem.Api/Controllers/AuditLogsController.cs; AccountingSystem.Api/Middleware/AuditMiddleware.cs; AccountingSystem.Api/Services/AuthSecurityAuditService.cs | Tenant audit logs screenshot |
| SuperAdmin Audit Logs | Records platform-level governance and security events for SuperAdmin review. | AccountingSystem.Client/Pages/SuperAdmin/AdminAuditLogs.razor; AccountingSystem.Client/Shared/Dialogs/SuperAdminAuditDetailsDialog.razor; AccountingSystem.Api/Controllers/SuperAdminController.cs | SuperAdmin audit logs screenshot; details dialog screenshot |
| Backup SuperAdmin Support | Allows SuperAdmin users to create and manage backup SuperAdmin accounts. | AccountingSystem.Client/Pages/SuperAdmin/GlobalUserManager.razor; AccountingSystem.Api/Controllers/SuperAdminController.cs; AccountingSystem.Shared/DTOs/SuperAdminDTOs.cs | Global Users / backup SuperAdmin screenshot |
| Last Active SuperAdmin Protection | Blocks disabling or deleting the last active SuperAdmin account. | AccountingSystem.Api/Controllers/SuperAdminController.cs; AccountingSystem.Client/Pages/SuperAdmin/GlobalUserManager.razor; AccountingSystem.Client/Pages/SuperAdmin/AdminAuditLogs.razor | Attempt to disable last active SuperAdmin blocked |
| Step-Up Verification for SuperAdmin Actions | Requires extra verification before sensitive SuperAdmin governance actions. | AccountingSystem.Client/Shared/Dialogs/SuperAdminStepUpDialog.razor; AccountingSystem.Client/Pages/SuperAdmin/GlobalUserManager.razor; AccountingSystem.Api/Controllers/SuperAdminController.cs; AccountingSystem.Shared/DTOs/SuperAdminDTOs.cs | Step-up verification dialog screenshot; audit log reason screenshot |
| Secure Configuration / Secret Handling | Uses placeholders/templates and local environment configuration for runtime secrets. | AccountingSystem.Api/appsettings.json; AccountingSystem.Api/.env.example; .gitignore; AccountingSystem.Api/Configuration/StartupConfigurationValidator.cs | .env.example screenshot; .gitignore screenshot; appsettings placeholder screenshot |
| CI Security Tooling | Provides build/test, static analysis, secret scan, and dependency evidence. | .github/workflows/security-tooling-evidence.yml; .github/workflows/codeql.yml; gitleaks.toml | GitHub Actions green checks; CodeQL result; Gitleaks evidence |
| PayMongo Webhook / Payment Callback Security | Validates PayMongo webhook signatures and replay window before accepting webhook requests. | AccountingSystem.Api/Controllers/PaymentController.cs; AccountingSystem.Api/Services/PaymentService.cs; AccountingSystem.Client/Pages/PaymentCallback.razor; AccountingSystem.Client/Services/PaymentClientService.cs | Payment callback test screenshot; audit log/payment record screenshot |
| JWT / Session Behavior | Issues JWTs during login/MFA login and stores the token in client localStorage until logout or token expiry. | AccountingSystem.Api/Services/AuthService.cs; AccountingSystem.Api/Services/JwtAuthTokenFactory.cs; AccountingSystem.Api/appsettings.json; AccountingSystem.Api/.env.example; AccountingSystem.Client/Services/TokenStorageService.cs; AccountingSystem.Client/Services/AuthService.cs; AccountingSystem.Client/Auth/CustomAuthStateProvider.cs | Login/logout test screenshot; documentation note explaining token expiry/localStorage behavior |

## Notes for Evidence Screenshots

- Blur or hide OTP codes.
- Blur or hide recovery codes.
- Do not show .env.
- Do not show SMTP password.
- Do not show PayMongo secret key.
- Do not show JWT secret.
- Do not show reCAPTCHA secret key.
- Public reCAPTCHA site key may appear in client code, but the secret key must not.
- Do not include real passwords in screenshots.
- Use GitHub Actions green checks as code-auditing evidence.
