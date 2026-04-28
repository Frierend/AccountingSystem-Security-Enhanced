# MFA Implementation

## Summary

Phase 8 adds optional MFA using Authenticator App TOTP, Email OTP, and recovery codes while keeping the existing JWT-based API/client architecture.

Supported authenticator apps:
- Google Authenticator
- Any app that supports standard `otpauth://totp/...` URIs and 6-digit TOTP codes

Supported email factor:
- 6-digit Email OTP sent to the user's confirmed email address

## Implementation Status

- **Implemented:** TOTP authenticator setup, MFA login challenge flow, and recovery-code support.
- **Implemented:** Email OTP MFA setup, login challenge, resend cooldown, verification attempts, and disable flow.
- **Known Limitation:** SMS OTP and push MFA are not implemented.
- **Known Limitation:** Email OTP challenges are stored in memory for demo use; pending codes are lost if the API restarts.
- **Known Limitation:** remember-device / remember-browser trust flow is not implemented.
- **Recommended Improvement:** add policy controls for mandatory MFA by role and security reporting for MFA enrollment/compliance.

## Flow

### 1. Password step

- Client calls `POST /api/auth/login` with email and password.
- If MFA is not enabled, the API returns the normal JWT `AuthResponseDTO`.
- If Authenticator App MFA or Email OTP MFA is enabled, the API returns:
  - `RequiresTwoFactor = true`
  - `TwoFactorChallengeToken`
  - available MFA methods
  - `Token = ""`

The challenge token is short-lived, signed, purpose-bound, and used only for the second login step.

### 2. MFA step

- Client navigates to `/mfa-login`.
- User enters one available method:
  - a 6-digit Google Authenticator code, or
  - a 6-digit Email OTP, or
  - a recovery code
- Client calls `POST /api/auth/login/mfa`.
- On success, the API issues the normal JWT with the existing claim contract.

### 3. MFA enrollment

- Authenticated user opens `/profile` and starts authenticator setup.
- API returns:
  - `SharedKey`
  - `AuthenticatorUri`
- Client renders the QR code from the `AuthenticatorUri`.
- User scans it with Google Authenticator or enters the manual key.
- User submits a 6-digit code.
- API verifies the code and only then enables MFA.
- API generates 10 recovery codes and returns them once.

### 4. MFA management

Authenticated users can:
- view MFA status
- reset the authenticator key
- regenerate recovery codes
- disable Authenticator App MFA
- enable Email OTP MFA after verifying a code sent to a confirmed email
- disable Email OTP MFA after re-authentication

Sensitive actions require exactly one re-authentication factor:
- current password, or
- current authenticator code, or
- a recovery code

## Setup Steps

1. Sign in to the app.
2. Open `/profile`.
3. In the security tab, choose `Set Up Authenticator`.
4. Scan the QR code with Google Authenticator.
5. If scanning is unavailable, enter the manual key in Google Authenticator.
6. Enter the 6-digit code shown by the app.
7. Save the recovery codes shown after verification.

## Recovery Codes

- Recovery codes are generated when MFA is first enabled.
- Regenerating recovery codes replaces the previous usable set.
- Recovery codes are single-use.
- Recovery-code login works only while MFA is enabled.
- The UI shows recovery codes only immediately after enable/regeneration.

## Email OTP Security

- OTP codes are generated with a secure random number generator.
- OTP codes expire after 5 minutes by default.
- OTP challenge state stores only a salted HMAC hash of the code in memory.
- OTP codes are one-time use.
- Verification is limited to 3 failed attempts per challenge by default.
- Resend cooldown is 60 seconds by default.
- OTP values are not written to application logs or audit details.

## Configuration

Safe non-secret configuration:

- `Mfa:AuthenticatorIssuer`
  - default: `AccountingSystem`
- `Mfa:LoginChallengeLifespanMinutes`
  - default: `5`
- `Mfa:EmailOtpExpirationMinutes`
  - default: `5`
- `Mfa:EmailOtpMaxVerificationAttempts`
  - default: `3`
- `Mfa:EmailOtpResendCooldownSeconds`
  - default: `60`

Rate-limit configuration was added for:

- `AuthSecurity:RateLimiting:LoginMfa`
- `AuthSecurity:RateLimiting:MfaManage`

## Key API Endpoints

- `POST /api/auth/login`
- `POST /api/auth/login/mfa`
- `POST /api/auth/login/mfa/email/send`
- `GET /api/auth/mfa`
- `POST /api/auth/mfa/authenticator/setup`
- `POST /api/auth/mfa/authenticator/reset`
- `POST /api/auth/mfa/authenticator/verify`
- `POST /api/auth/mfa/recovery-codes/regenerate`
- `POST /api/auth/mfa/disable`
- `POST /api/auth/mfa/email/setup`
- `POST /api/auth/mfa/email/verify`
- `POST /api/auth/mfa/email/disable`

## Security Notes

- TOTP codes are verified through ASP.NET Core Identity and are never stored.
- Recovery codes are managed through Identity and are invalidated on use.
- Secrets, QR URIs, TOTP codes, Email OTP codes, and recovery codes are not written to audit logs.
- If both Authenticator App MFA and Email OTP MFA are enabled, the login challenge prioritizes the authenticator app and offers Email OTP as a backup option.
- SuperAdmin has no MFA exemption in this phase. If MFA is enabled on the account, the second step is required.
- **Known Limitation:** MFA is currently optional and policy-based enforcement is not yet implemented across all role scenarios.
- **Recommended Improvement:** add administrative reporting dashboards for MFA enrollment and recovery-code events.
