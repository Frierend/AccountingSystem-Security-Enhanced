using AccountingSystem.API.Identity;
using AccountingSystem.API.Security;
using AccountingSystem.Shared.DTOs;

namespace AccountingSystem.API.Services.Interfaces
{
    public interface IMfaService
    {
        Task<MfaStatusDTO> GetStatusAsync(int legacyUserId);

        Task<MfaSetupDTO> BeginAuthenticatorSetupAsync(int legacyUserId);

        Task<MfaSetupDTO> ResetAuthenticatorAsync(int legacyUserId, MfaReauthenticationDTO dto);

        Task<RecoveryCodesDTO> VerifyAuthenticatorSetupAsync(int legacyUserId, VerifyAuthenticatorSetupDTO dto);

        Task<RecoveryCodesDTO> RegenerateRecoveryCodesAsync(int legacyUserId, MfaReauthenticationDTO dto);

        Task DisableAsync(int legacyUserId, MfaReauthenticationDTO dto);

        Task<bool> IsEmailOtpEnabledAsync(ApplicationUser identityUser);

        Task SendEmailOtpSetupCodeAsync(int legacyUserId);

        Task EnableEmailOtpAsync(int legacyUserId, VerifyEmailOtpMfaDTO dto);

        Task DisableEmailOtpAsync(int legacyUserId, MfaReauthenticationDTO dto);

        Task SendLoginEmailOtpAsync(SendLoginEmailOtpDTO dto);

        Task<MfaLoginVerificationResult> VerifyLoginChallengeAsync(LoginMfaDTO dto);
    }
}
