using AccountingSystem.API.Configuration;
using AccountingSystem.API.Identity;
using AccountingSystem.API.Security;
using AccountingSystem.API.Services.Interfaces;
using AccountingSystem.Shared.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AccountingSystem.API.Services
{
    public class MfaService : IMfaService
    {
        private const int RecoveryCodeCount = 10;
        private const string EmailOtpLoginProvider = "AccSysEmailOtpMfa";
        private const string EmailOtpEnabledTokenName = "Enabled";
        private const string EmailOtpSetupPurpose = "email-otp-setup";
        private const string EmailOtpLoginPurpose = "email-otp-login";
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IIdentityAccountService _identityAccountService;
        private readonly ILoginChallengeTokenService _loginChallengeTokenService;
        private readonly IAuthSecurityAuditService _auditService;
        private readonly IEmailOtpChallengeStore _emailOtpChallengeStore;
        private readonly IAccountEmailService _accountEmailService;
        private readonly string _issuer;
        private readonly int _emailOtpExpirationMinutes;
        private readonly int _emailOtpMaxVerificationAttempts;
        private readonly int _emailOtpResendCooldownSeconds;

        public MfaService(
            UserManager<ApplicationUser> userManager,
            IIdentityAccountService identityAccountService,
            ILoginChallengeTokenService loginChallengeTokenService,
            IAuthSecurityAuditService auditService,
            IEmailOtpChallengeStore emailOtpChallengeStore,
            IAccountEmailService accountEmailService,
            IOptions<MfaSettings> settings)
        {
            _userManager = userManager;
            _identityAccountService = identityAccountService;
            _loginChallengeTokenService = loginChallengeTokenService;
            _auditService = auditService;
            _emailOtpChallengeStore = emailOtpChallengeStore;
            _accountEmailService = accountEmailService;
            _issuer = string.IsNullOrWhiteSpace(settings.Value.AuthenticatorIssuer)
                ? "AccountingSystem"
                : settings.Value.AuthenticatorIssuer.Trim();
            _emailOtpExpirationMinutes = settings.Value.EmailOtpExpirationMinutes > 0
                ? settings.Value.EmailOtpExpirationMinutes
                : 5;
            _emailOtpMaxVerificationAttempts = settings.Value.EmailOtpMaxVerificationAttempts > 0
                ? settings.Value.EmailOtpMaxVerificationAttempts
                : 3;
            _emailOtpResendCooldownSeconds = settings.Value.EmailOtpResendCooldownSeconds > 0
                ? settings.Value.EmailOtpResendCooldownSeconds
                : 60;
        }

        public async Task<MfaStatusDTO> GetStatusAsync(int legacyUserId)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
            var isTwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(identityUser);

            return new MfaStatusDTO
            {
                IsTwoFactorEnabled = isTwoFactorEnabled,
                IsAuthenticatorAppEnabled = isTwoFactorEnabled,
                HasAuthenticatorKey = !string.IsNullOrWhiteSpace(authenticatorKey),
                RecoveryCodesLeft = isTwoFactorEnabled
                    ? await _userManager.CountRecoveryCodesAsync(identityUser)
                    : 0,
                IsEmailOtpEnabled = await IsEmailOtpEnabledAsync(identityUser),
                IsEmailConfirmed = await _userManager.IsEmailConfirmedAsync(identityUser),
                Email = identityUser.Email ?? string.Empty
            };
        }

        public async Task<MfaSetupDTO> BeginAuthenticatorSetupAsync(int legacyUserId)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            var setup = await BuildSetupDtoAsync(identityUser);

            await _auditService.WriteAsync(
                "AUTH-MFA-SETUP-STARTED",
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: setup.IsTwoFactorEnabled ? "AlreadyEnabled" : "PendingVerification");

            return setup;
        }

        public async Task<MfaSetupDTO> ResetAuthenticatorAsync(int legacyUserId, MfaReauthenticationDTO dto)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            await ReauthenticateAsync(identityUser, dto);

            identityUser.UpdatedAt = DateTime.UtcNow;
            EnsureIdentitySucceeded(await _userManager.SetTwoFactorEnabledAsync(identityUser, false), "DisableTwoFactorForReset");

            identityUser = await RequireIdentityUserByIdAsync(identityUser.Id);
            identityUser.UpdatedAt = DateTime.UtcNow;
            EnsureIdentitySucceeded(await _userManager.ResetAuthenticatorKeyAsync(identityUser), "ResetAuthenticatorKey");

            var refreshedUser = await RequireIdentityUserByIdAsync(identityUser.Id);
            var setup = await BuildSetupDtoAsync(refreshedUser);

            await _auditService.WriteAsync(
                "AUTH-MFA-RESET",
                userId: refreshedUser.LegacyUserId,
                companyId: refreshedUser.CompanyId,
                email: refreshedUser.Email,
                reason: "AuthenticatorReset");

            return setup;
        }

        public async Task<RecoveryCodesDTO> VerifyAuthenticatorSetupAsync(int legacyUserId, VerifyAuthenticatorSetupDTO dto)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            await EnsureAuthenticatorKeyAsync(identityUser);

            var sanitizedCode = NormalizeAuthenticatorCode(dto.Code);
            if (!IsValidAuthenticatorCode(sanitizedCode) ||
                !await _userManager.VerifyTwoFactorTokenAsync(
                    identityUser,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    sanitizedCode))
            {
                await _auditService.WriteAsync(
                    "AUTH-MFA-ENABLE-FAILURE",
                    userId: identityUser.LegacyUserId,
                    companyId: identityUser.CompanyId,
                    email: identityUser.Email,
                    reason: "InvalidAuthenticatorCode");
                throw new InvalidOperationException("The verification code is invalid. Please try again.");
            }

            identityUser.UpdatedAt = DateTime.UtcNow;
            EnsureIdentitySucceeded(await _userManager.SetTwoFactorEnabledAsync(identityUser, true), "EnableTwoFactor");

            var refreshedUser = await RequireIdentityUserByIdAsync(identityUser.Id);
            var recoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(refreshedUser, RecoveryCodeCount)
                ?? Enumerable.Empty<string>())
                .ToList();

            await _auditService.WriteAsync(
                "AUTH-MFA-ENABLED",
                userId: refreshedUser.LegacyUserId,
                companyId: refreshedUser.CompanyId,
                email: refreshedUser.Email,
                reason: "AuthenticatorVerified");

            return new RecoveryCodesDTO
            {
                RecoveryCodes = recoveryCodes
            };
        }

        public async Task<RecoveryCodesDTO> RegenerateRecoveryCodesAsync(int legacyUserId, MfaReauthenticationDTO dto)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            await ReauthenticateAsync(identityUser, dto);

            if (!await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                throw new InvalidOperationException("Two-factor authentication is not enabled for this account.");
            }

            var refreshedUser = await RequireIdentityUserByIdAsync(identityUser.Id);
            var recoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(refreshedUser, RecoveryCodeCount)
                ?? Enumerable.Empty<string>())
                .ToList();

            await _auditService.WriteAsync(
                "AUTH-MFA-RECOVERY-CODES-REGENERATED",
                userId: refreshedUser.LegacyUserId,
                companyId: refreshedUser.CompanyId,
                email: refreshedUser.Email,
                reason: "RecoveryCodesRegenerated");

            return new RecoveryCodesDTO
            {
                RecoveryCodes = recoveryCodes
            };
        }

        public async Task DisableAsync(int legacyUserId, MfaReauthenticationDTO dto)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            await ReauthenticateAsync(identityUser, dto);

            identityUser.UpdatedAt = DateTime.UtcNow;
            EnsureIdentitySucceeded(await _userManager.SetTwoFactorEnabledAsync(identityUser, false), "DisableTwoFactor");

            identityUser = await RequireIdentityUserByIdAsync(identityUser.Id);
            identityUser.UpdatedAt = DateTime.UtcNow;
            EnsureIdentitySucceeded(await _userManager.ResetAuthenticatorKeyAsync(identityUser), "ResetAuthenticatorKeyOnDisable");

            await _auditService.WriteAsync(
                "AUTH-MFA-DISABLED",
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: "AuthenticatorDisabled");
        }

        public async Task<bool> IsEmailOtpEnabledAsync(ApplicationUser identityUser)
        {
            var value = await _userManager.GetAuthenticationTokenAsync(
                identityUser,
                EmailOtpLoginProvider,
                EmailOtpEnabledTokenName);

            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public async Task SendEmailOtpSetupCodeAsync(int legacyUserId)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            await EnsureEmailConfirmedForEmailOtpAsync(identityUser);

            var code = GenerateEmailOtpCode();
            var issueResult = _emailOtpChallengeStore.Issue(
                BuildEmailOtpSetupChallengeKey(identityUser.Id),
                identityUser.Id,
                identityUser.LegacyUserId ?? legacyUserId,
                code,
                GetEmailOtpExpiration(),
                GetEmailOtpResendCooldown());

            if (!issueResult.Succeeded)
            {
                await _auditService.WriteAsync(
                    "AUTH-EMAIL-OTP-RATE-LIMITED",
                    userId: identityUser.LegacyUserId,
                    companyId: identityUser.CompanyId,
                    email: identityUser.Email,
                    reason: "SetupResendCooldown",
                    policy: "EmailOtpSetup");
                throw new InvalidOperationException("Please wait before requesting another email verification code.");
            }

            await _accountEmailService.SendEmailOtpAsync(
                identityUser.Email!,
                identityUser.FullName,
                code,
                _emailOtpExpirationMinutes);

            await _auditService.WriteAsync(
                "AUTH-EMAIL-OTP-SENT",
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: "Setup",
                policy: "EmailOtpSetup");
        }

        public async Task EnableEmailOtpAsync(int legacyUserId, VerifyEmailOtpMfaDTO dto)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            await EnsureEmailConfirmedForEmailOtpAsync(identityUser);

            var verificationResult = _emailOtpChallengeStore.Verify(
                BuildEmailOtpSetupChallengeKey(identityUser.Id),
                identityUser.Id,
                identityUser.LegacyUserId ?? legacyUserId,
                dto.Code,
                _emailOtpMaxVerificationAttempts);

            await HandleEmailOtpVerificationFailureAsync(
                verificationResult,
                identityUser,
                "Setup",
                "The email verification code is invalid or expired. Please request a new code.");

            EnsureIdentitySucceeded(
                await _userManager.SetAuthenticationTokenAsync(
                    identityUser,
                    EmailOtpLoginProvider,
                    EmailOtpEnabledTokenName,
                    "true"),
                "EnableEmailOtpMfa");

            await _auditService.WriteAsync(
                "AUTH-EMAIL-OTP-ENABLED",
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: "EmailOtpVerified");
        }

        public async Task DisableEmailOtpAsync(int legacyUserId, MfaReauthenticationDTO dto)
        {
            var identityUser = await RequireIdentityUserAsync(legacyUserId);
            await ReauthenticateAsync(identityUser, dto);

            EnsureIdentitySucceeded(
                await _userManager.RemoveAuthenticationTokenAsync(
                    identityUser,
                    EmailOtpLoginProvider,
                    EmailOtpEnabledTokenName),
                "DisableEmailOtpMfa");

            await _auditService.WriteAsync(
                "AUTH-EMAIL-OTP-DISABLED",
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: "EmailOtpDisabled");
        }

        public async Task SendLoginEmailOtpAsync(SendLoginEmailOtpDTO dto)
        {
            var challenge = _loginChallengeTokenService.Validate(dto.ChallengeToken);
            var identityUser = await RequireChallengeIdentityUserAsync(challenge);
            await EnsureEmailOtpCanBeUsedForLoginAsync(identityUser);

            var code = GenerateEmailOtpCode();
            var issueResult = _emailOtpChallengeStore.Issue(
                BuildEmailOtpLoginChallengeKey(dto.ChallengeToken),
                identityUser.Id,
                identityUser.LegacyUserId ?? challenge.LegacyUserId,
                code,
                GetEmailOtpExpiration(),
                GetEmailOtpResendCooldown());

            if (!issueResult.Succeeded)
            {
                await _auditService.WriteAsync(
                    "AUTH-EMAIL-OTP-RATE-LIMITED",
                    userId: identityUser.LegacyUserId,
                    companyId: identityUser.CompanyId,
                    email: identityUser.Email,
                    reason: "LoginResendCooldown",
                    policy: "EmailOtpLogin");
                throw new AuthFailureException(
                    "EmailOtpRateLimited",
                    "Please wait before requesting another email verification code.",
                    StatusCodes.Status429TooManyRequests);
            }

            await _accountEmailService.SendEmailOtpAsync(
                identityUser.Email!,
                identityUser.FullName,
                code,
                _emailOtpExpirationMinutes);

            await _auditService.WriteAsync(
                "AUTH-EMAIL-OTP-SENT",
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: "Login",
                policy: "EmailOtpLogin");
        }

        public async Task<MfaLoginVerificationResult> VerifyLoginChallengeAsync(LoginMfaDTO dto)
        {
            var challenge = _loginChallengeTokenService.Validate(dto.ChallengeToken);
            var identityUser = await RequireChallengeIdentityUserAsync(challenge);
            var authenticatorEnabled = await _userManager.GetTwoFactorEnabledAsync(identityUser);
            var emailOtpEnabled = await IsEmailOtpEnabledAsync(identityUser);
            if (!authenticatorEnabled && !emailOtpEnabled)
            {
                await _auditService.WriteAsync(
                    "AUTH-MFA-LOGIN-FAILURE",
                    userId: identityUser.LegacyUserId,
                    companyId: identityUser.CompanyId,
                    email: identityUser.Email,
                    reason: "ChallengeMfaDisabled");
                throw new AuthFailureException(
                    "MfaChallengeInvalid",
                    "The sign-in verification session is invalid or expired. Please sign in again.");
            }

            var hasAuthenticatorCode = !string.IsNullOrWhiteSpace(dto.TwoFactorCode);
            var hasRecoveryCode = !string.IsNullOrWhiteSpace(dto.RecoveryCode);
            if (hasAuthenticatorCode == hasRecoveryCode)
            {
                throw new AuthFailureException(
                    "MfaFactorSelectionInvalid",
                    "Enter either a 6-digit authenticator code or a recovery code.",
                    StatusCodes.Status400BadRequest);
            }

            var requestedMethod = NormalizeMfaMethod(dto.Method);
            if (hasRecoveryCode)
            {
                if (!authenticatorEnabled)
                {
                    await _auditService.WriteAsync(
                        "AUTH-MFA-RECOVERY-CODE-FAILURE",
                        userId: identityUser.LegacyUserId,
                        companyId: identityUser.CompanyId,
                        email: identityUser.Email,
                        reason: "AuthenticatorMfaDisabled");
                    throw new AuthFailureException(
                        "InvalidRecoveryCode",
                        "The recovery code is invalid. Please try again.");
                }

                var recoveryResult = await RedeemRecoveryCodeAsync(identityUser, dto.RecoveryCode);
                if (!recoveryResult.Succeeded)
                {
                    await _auditService.WriteAsync(
                        "AUTH-MFA-RECOVERY-CODE-FAILURE",
                        userId: identityUser.LegacyUserId,
                        companyId: identityUser.CompanyId,
                        email: identityUser.Email,
                        reason: "InvalidRecoveryCode");
                    throw new AuthFailureException(
                        "InvalidRecoveryCode",
                        "The recovery code is invalid. Please try again.");
                }

                return new MfaLoginVerificationResult(identityUser, true, MfaLoginMethods.RecoveryCode);
            }

            if (requestedMethod == MfaLoginMethods.EmailOtp)
            {
                if (!emailOtpEnabled || !await _userManager.IsEmailConfirmedAsync(identityUser))
                {
                    await _auditService.WriteAsync(
                        "AUTH-EMAIL-OTP-VERIFY-FAILURE",
                        userId: identityUser.LegacyUserId,
                        companyId: identityUser.CompanyId,
                        email: identityUser.Email,
                        reason: "EmailOtpUnavailable",
                        policy: "EmailOtpLogin");
                    throw new AuthFailureException(
                        "InvalidEmailOtpCode",
                        "The email verification code is invalid or expired. Please request a new code.");
                }

                var verificationResult = _emailOtpChallengeStore.Verify(
                    BuildEmailOtpLoginChallengeKey(dto.ChallengeToken),
                    identityUser.Id,
                    identityUser.LegacyUserId ?? challenge.LegacyUserId,
                    dto.TwoFactorCode,
                    _emailOtpMaxVerificationAttempts);

                await HandleEmailOtpVerificationFailureAsync(
                    verificationResult,
                    identityUser,
                    "Login",
                    "The email verification code is invalid or expired. Please request a new code.");

                return new MfaLoginVerificationResult(identityUser, false, MfaLoginMethods.EmailOtp);
            }

            if (requestedMethod == MfaLoginMethods.AuthenticatorApp)
            {
                if (!authenticatorEnabled)
                {
                    await _auditService.WriteAsync(
                        "AUTH-MFA-LOGIN-FAILURE",
                        userId: identityUser.LegacyUserId,
                        companyId: identityUser.CompanyId,
                        email: identityUser.Email,
                        reason: "AuthenticatorMfaDisabled");
                    throw new AuthFailureException(
                        "InvalidTwoFactorCode",
                        "The verification code is invalid. Please try again.");
                }

                var sanitizedCode = NormalizeAuthenticatorCode(dto.TwoFactorCode);
                if (!IsValidAuthenticatorCode(sanitizedCode) ||
                    !await _userManager.VerifyTwoFactorTokenAsync(
                        identityUser,
                        _userManager.Options.Tokens.AuthenticatorTokenProvider,
                        sanitizedCode))
                {
                    await _auditService.WriteAsync(
                        "AUTH-MFA-LOGIN-FAILURE",
                        userId: identityUser.LegacyUserId,
                        companyId: identityUser.CompanyId,
                        email: identityUser.Email,
                        reason: "InvalidAuthenticatorCode");
                    throw new AuthFailureException(
                        "InvalidTwoFactorCode",
                        "The verification code is invalid. Please try again.");
                }

                return new MfaLoginVerificationResult(identityUser, false, MfaLoginMethods.AuthenticatorApp);
            }

            throw new AuthFailureException(
                "MfaFactorSelectionInvalid",
                "Choose a valid verification method.",
                StatusCodes.Status400BadRequest);
        }

        private async Task ReauthenticateAsync(ApplicationUser identityUser, MfaReauthenticationDTO dto)
        {
            var methodsUsed = CountProvidedMethods(dto);
            if (methodsUsed != 1)
            {
                throw new InvalidOperationException("Provide exactly one verification method: current password, authenticator code, or recovery code.");
            }

            if (!string.IsNullOrWhiteSpace(dto.CurrentPassword))
            {
                if (!await _userManager.CheckPasswordAsync(identityUser, dto.CurrentPassword))
                {
                    await _auditService.WriteAsync(
                        "AUTH-MFA-REAUTH-FAILURE",
                        userId: identityUser.LegacyUserId,
                        companyId: identityUser.CompanyId,
                        email: identityUser.Email,
                        reason: "InvalidCurrentPassword");
                    throw new InvalidOperationException("The current password is incorrect.");
                }

                return;
            }

            if (!await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                throw new InvalidOperationException("Two-factor authentication is not enabled for this account. Use your current password to continue.");
            }

            if (!string.IsNullOrWhiteSpace(dto.TwoFactorCode))
            {
                var sanitizedCode = NormalizeAuthenticatorCode(dto.TwoFactorCode);
                if (!IsValidAuthenticatorCode(sanitizedCode) ||
                    !await _userManager.VerifyTwoFactorTokenAsync(
                        identityUser,
                        _userManager.Options.Tokens.AuthenticatorTokenProvider,
                        sanitizedCode))
                {
                    await _auditService.WriteAsync(
                        "AUTH-MFA-REAUTH-FAILURE",
                        userId: identityUser.LegacyUserId,
                        companyId: identityUser.CompanyId,
                        email: identityUser.Email,
                        reason: "InvalidAuthenticatorCode");
                    throw new InvalidOperationException("The authenticator code is invalid.");
                }

                return;
            }

            var recoveryResult = await RedeemRecoveryCodeAsync(identityUser, dto.RecoveryCode);
            if (!recoveryResult.Succeeded)
            {
                await _auditService.WriteAsync(
                    "AUTH-MFA-REAUTH-FAILURE",
                    userId: identityUser.LegacyUserId,
                    companyId: identityUser.CompanyId,
                    email: identityUser.Email,
                    reason: "InvalidRecoveryCode");
                throw new InvalidOperationException("The recovery code is invalid.");
            }
        }

        private async Task<ApplicationUser> RequireChallengeIdentityUserAsync(LoginChallengeTokenPayload challenge)
        {
            var identityUser = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == challenge.IdentityUserId);
            if (identityUser == null)
            {
                await _auditService.WriteAsync(
                    "AUTH-MFA-LOGIN-FAILURE",
                    userId: challenge.LegacyUserId,
                    reason: "ChallengeIdentityUserNotFound");
                throw new AuthFailureException(
                    "MfaChallengeInvalid",
                    "The sign-in verification session is invalid or expired. Please sign in again.");
            }

            if (identityUser.LegacyUserId != challenge.LegacyUserId)
            {
                await _auditService.WriteAsync(
                    "AUTH-MFA-LOGIN-FAILURE",
                    userId: identityUser.LegacyUserId,
                    companyId: identityUser.CompanyId,
                    email: identityUser.Email,
                    reason: "ChallengeLegacyUserMismatch");
                throw new AuthFailureException(
                    "MfaChallengeInvalid",
                    "The sign-in verification session is invalid or expired. Please sign in again.");
            }

            return identityUser;
        }

        private async Task EnsureEmailConfirmedForEmailOtpAsync(ApplicationUser identityUser)
        {
            if (!string.IsNullOrWhiteSpace(identityUser.Email) &&
                await _userManager.IsEmailConfirmedAsync(identityUser))
            {
                return;
            }

            await _auditService.WriteAsync(
                "AUTH-EMAIL-OTP-VERIFY-FAILURE",
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: "EmailNotConfirmed",
                policy: "EmailOtp");
            throw new InvalidOperationException("Confirm your email address before enabling Email OTP MFA.");
        }

        private async Task EnsureEmailOtpCanBeUsedForLoginAsync(ApplicationUser identityUser)
        {
            if (await IsEmailOtpEnabledAsync(identityUser) &&
                !string.IsNullOrWhiteSpace(identityUser.Email) &&
                await _userManager.IsEmailConfirmedAsync(identityUser))
            {
                return;
            }

            await _auditService.WriteAsync(
                "AUTH-EMAIL-OTP-VERIFY-FAILURE",
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: "EmailOtpUnavailable",
                policy: "EmailOtpLogin");
            throw new AuthFailureException(
                "EmailOtpUnavailable",
                "The email verification code is unavailable. Please use another verification method.",
                StatusCodes.Status400BadRequest);
        }

        private async Task HandleEmailOtpVerificationFailureAsync(
            EmailOtpVerificationResult verificationResult,
            ApplicationUser identityUser,
            string context,
            string publicFailureMessage)
        {
            if (verificationResult.Succeeded)
            {
                await _auditService.WriteAsync(
                    "AUTH-EMAIL-OTP-VERIFY-SUCCESS",
                    userId: identityUser.LegacyUserId,
                    companyId: identityUser.CompanyId,
                    email: identityUser.Email,
                    reason: context,
                    policy: $"EmailOtp{context}");
                return;
            }

            var action = verificationResult.Status switch
            {
                EmailOtpVerificationStatus.Expired => "AUTH-EMAIL-OTP-EXPIRED",
                EmailOtpVerificationStatus.TooManyAttempts => "AUTH-EMAIL-OTP-RATE-LIMITED",
                _ => "AUTH-EMAIL-OTP-VERIFY-FAILURE"
            };

            await _auditService.WriteAsync(
                action,
                userId: identityUser.LegacyUserId,
                companyId: identityUser.CompanyId,
                email: identityUser.Email,
                reason: verificationResult.Status.ToString(),
                failedAttempts: verificationResult.FailedAttempts,
                policy: $"EmailOtp{context}");

            var message = verificationResult.Status == EmailOtpVerificationStatus.TooManyAttempts
                ? "Too many email verification attempts. Please request a new code."
                : publicFailureMessage;

            if (string.Equals(context, "Login", StringComparison.Ordinal))
            {
                throw new AuthFailureException("InvalidEmailOtpCode", message);
            }

            throw new InvalidOperationException(message);
        }

        private async Task<MfaSetupDTO> BuildSetupDtoAsync(ApplicationUser identityUser)
        {
            var authenticatorKey = await EnsureAuthenticatorKeyAsync(identityUser);
            return new MfaSetupDTO
            {
                IsTwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(identityUser),
                SharedKey = authenticatorKey,
                AuthenticatorUri = BuildAuthenticatorUri(identityUser.Email ?? identityUser.UserName ?? string.Empty, authenticatorKey)
            };
        }

        private async Task<string> EnsureAuthenticatorKeyAsync(ApplicationUser identityUser)
        {
            var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
            if (!string.IsNullOrWhiteSpace(authenticatorKey))
            {
                return authenticatorKey;
            }

            identityUser.UpdatedAt = DateTime.UtcNow;
            EnsureIdentitySucceeded(await _userManager.ResetAuthenticatorKeyAsync(identityUser), "InitializeAuthenticatorKey");
            var refreshedUser = await RequireIdentityUserByIdAsync(identityUser.Id);
            authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(refreshedUser);
            if (string.IsNullOrWhiteSpace(authenticatorKey))
            {
                throw new InvalidOperationException("Unable to initialize the authenticator key for this account.");
            }

            return authenticatorKey;
        }

        private string BuildAuthenticatorUri(string email, string sharedKey)
        {
            var label = Uri.EscapeDataString($"{_issuer}:{email}");
            var encodedIssuer = Uri.EscapeDataString(_issuer);
            return $"otpauth://totp/{label}?secret={sharedKey}&issuer={encodedIssuer}";
        }

        private async Task<ApplicationUser> RequireIdentityUserAsync(int legacyUserId)
        {
            return await _identityAccountService.FindByLegacyUserIdAsync(legacyUserId)
                ?? throw new InvalidOperationException("Identity user was not found for this account.");
        }

        private async Task<ApplicationUser> RequireIdentityUserByIdAsync(int identityUserId)
        {
            return await _userManager.Users.FirstOrDefaultAsync(u => u.Id == identityUserId)
                ?? throw new InvalidOperationException("Identity user was not found for this account.");
        }

        private static int CountProvidedMethods(MfaReauthenticationDTO dto)
        {
            var count = 0;
            if (!string.IsNullOrWhiteSpace(dto.CurrentPassword))
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(dto.TwoFactorCode))
            {
                count++;
            }

            if (!string.IsNullOrWhiteSpace(dto.RecoveryCode))
            {
                count++;
            }

            return count;
        }

        private static string NormalizeAuthenticatorCode(string code)
        {
            return new string(code.Where(char.IsDigit).ToArray());
        }

        private static string NormalizeRecoveryCode(string code)
        {
            return new string(code
                .Where(character => !char.IsWhiteSpace(character) && character != '-')
                .ToArray());
        }

        private async Task<IdentityResult> RedeemRecoveryCodeAsync(ApplicationUser identityUser, string recoveryCode)
        {
            var trimmedRecoveryCode = recoveryCode.Trim();
            if (string.IsNullOrWhiteSpace(trimmedRecoveryCode))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "InvalidRecoveryCode",
                    Description = "The recovery code is invalid."
                });
            }

            var normalizedRecoveryCode = NormalizeRecoveryCode(trimmedRecoveryCode);
            var candidates = new[] { trimmedRecoveryCode, normalizedRecoveryCode }
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.Ordinal);

            IdentityResult? lastResult = null;
            foreach (var candidate in candidates)
            {
                lastResult = await _userManager.RedeemTwoFactorRecoveryCodeAsync(identityUser, candidate);
                if (lastResult.Succeeded)
                {
                    return lastResult;
                }
            }

            return lastResult ?? IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidRecoveryCode",
                Description = "The recovery code is invalid."
            });
        }

        private static bool IsValidAuthenticatorCode(string code)
        {
            return code.Length == 6 && code.All(char.IsDigit);
        }

        private TimeSpan GetEmailOtpExpiration()
        {
            return TimeSpan.FromMinutes(_emailOtpExpirationMinutes);
        }

        private TimeSpan GetEmailOtpResendCooldown()
        {
            return TimeSpan.FromSeconds(_emailOtpResendCooldownSeconds);
        }

        private static string GenerateEmailOtpCode()
        {
            return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        }

        private static string BuildEmailOtpSetupChallengeKey(int identityUserId)
        {
            return $"{EmailOtpSetupPurpose}:{identityUserId}";
        }

        private static string BuildEmailOtpLoginChallengeKey(string challengeToken)
        {
            var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(challengeToken ?? string.Empty)));
            return $"{EmailOtpLoginPurpose}:{tokenHash}";
        }

        private static string NormalizeMfaMethod(string? method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return MfaLoginMethods.AuthenticatorApp;
            }

            if (string.Equals(method, MfaLoginMethods.EmailOtp, StringComparison.OrdinalIgnoreCase))
            {
                return MfaLoginMethods.EmailOtp;
            }

            if (string.Equals(method, MfaLoginMethods.RecoveryCode, StringComparison.OrdinalIgnoreCase))
            {
                return MfaLoginMethods.RecoveryCode;
            }

            if (string.Equals(method, MfaLoginMethods.AuthenticatorApp, StringComparison.OrdinalIgnoreCase))
            {
                return MfaLoginMethods.AuthenticatorApp;
            }

            return method.Trim();
        }

        private static void EnsureIdentitySucceeded(IdentityResult result, string operation)
        {
            if (result.Succeeded)
            {
                return;
            }

            var details = string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
            throw new InvalidOperationException($"Identity operation '{operation}' failed: {details}");
        }
    }
}
