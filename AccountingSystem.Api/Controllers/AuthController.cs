using AccountingSystem.API.Configuration;
using AccountingSystem.API.Security;
using AccountingSystem.API.Services.Interfaces;
using AccountingSystem.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace AccountingSystem.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController>? logger = null)
        {
            _authService = authService;
            _logger = logger ?? NullLogger<AuthController>.Instance;
        }

        [HttpPost("login")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.Login)]
        public async Task<IActionResult> Login([FromBody] LoginDTO loginDto)
        {
            try
            {
                var response = await _authService.LoginAsync(loginDto);
                return Ok(response);
            }
            catch (AuthFailureException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Login attempt failed for {Email}. StatusCode: {StatusCode}.",
                    loginDto.Email,
                    ex.StatusCode);

                if (ex.StatusCode == StatusCodes.Status401Unauthorized)
                {
                    if (ex.RequiresRecaptcha)
                    {
                        return Unauthorized(new { error = ex.PublicMessage, requiresRecaptcha = true });
                    }

                    return Unauthorized(new { error = ex.PublicMessage });
                }

                if (ex.RequiresRecaptcha)
                {
                    return StatusCode(ex.StatusCode, new { error = ex.PublicMessage, requiresRecaptcha = true });
                }

                return StatusCode(ex.StatusCode, new { error = ex.PublicMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during login for {Email}.", loginDto.Email);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred. Please try again later." });
            }
        }

        [HttpPost("login/mfa")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.LoginMfa)]
        public async Task<IActionResult> LoginWithMfa([FromBody] LoginMfaDTO dto)
        {
            try
            {
                var response = await _authService.CompleteMfaLoginAsync(dto);
                return Ok(response);
            }
            catch (AuthFailureException ex)
            {
                _logger.LogWarning(
                    ex,
                    "MFA login attempt failed for challenge token {ChallengeToken}. StatusCode: {StatusCode}.",
                    dto.ChallengeToken,
                    ex.StatusCode);

                if (ex.StatusCode == StatusCodes.Status401Unauthorized)
                {
                    return Unauthorized(new { error = ex.PublicMessage });
                }

                return StatusCode(ex.StatusCode, new { error = ex.PublicMessage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during MFA login for challenge token {ChallengeToken}.", dto.ChallengeToken);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred. Please try again later." });
            }
        }

        [HttpPost("register-company")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.RegisterCompany)]
        public async Task<IActionResult> RegisterCompany([FromBody] CompanyRegisterDTO dto)
        {
            try
            {
                var response = await _authService.RegisterCompanyAsync(dto);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Company registration failed for admin email {AdminEmail}.", dto.AdminEmail);
                return BadRequest(new { error = "Unable to complete registration. Please verify your input and try again." });
            }
        }

        [HttpGet("profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userId = GetCurrentUserId();
                var response = await _authService.GetCurrentProfileAsync(userId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load profile for current user.");
                return BadRequest(new { error = "Unable to load profile information. Please try again." });
            }
        }

        [HttpPut("profile")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDTO dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _authService.UpdateProfileAsync(userId, dto);
                return Ok(new { message = "Profile updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update profile for user {UserId}.", TryGetCurrentUserId());
                return BadRequest(new { error = "Unable to update profile. Please verify your input and try again." });
            }
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.ForgotPassword)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDTO dto)
        {
            await _authService.SendPasswordResetAsync(dto);
            return Ok(new { message = "If the account exists, a password reset link has been sent." });
        }

        [HttpPost("confirm-email")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.ConfirmEmail)]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDTO dto)
        {
            try
            {
                await _authService.ConfirmEmailAsync(dto);
                return Ok(new { message = "Email confirmed successfully. You can sign in now." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email confirmation failed.");
                return BadRequest(new { error = "Unable to confirm email. The confirmation request may be invalid or expired." });
            }
        }

        [HttpPost("resend-confirmation")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.ResendConfirmation)]
        public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationDTO dto)
        {
            await _authService.ResendConfirmationAsync(dto);
            return Ok(new { message = "If the account exists and still needs confirmation, a new confirmation link has been sent." });
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.ResetPassword)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDTO dto)
        {
            try
            {
                await _authService.ResetPasswordAsync(dto);
                return Ok(new { message = "Password reset successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Password reset failed.");
                return BadRequest(new { error = "Unable to reset password. The reset request may be invalid or expired." });
            }
        }

        [HttpPut("password")]
        [Authorize]
        [EnableRateLimiting(AuthRateLimitPolicyNames.ChangePassword)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDTO dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _authService.ChangePasswordAsync(userId, dto);
                return Ok(new { message = "Password changed successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to change password for user {UserId}.", TryGetCurrentUserId());
                return BadRequest(new { error = "Unable to change password. Please verify your current password and try again." });
            }
        }

        [HttpGet("mfa")]
        [Authorize]
        [EnableRateLimiting(AuthRateLimitPolicyNames.MfaManage)]
        public async Task<IActionResult> GetMfaStatus()
        {
            try
            {
                var userId = GetCurrentUserId();
                var response = await _authService.GetMfaStatusAsync(userId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get MFA status for user {UserId}.", TryGetCurrentUserId());
                return BadRequest(new { error = "Unable to retrieve MFA status. Please try again." });
            }
        }

        [HttpPost("mfa/authenticator/setup")]
        [Authorize]
        [EnableRateLimiting(AuthRateLimitPolicyNames.MfaManage)]
        public async Task<IActionResult> BeginAuthenticatorSetup()
        {
            try
            {
                var userId = GetCurrentUserId();
                var response = await _authService.BeginAuthenticatorSetupAsync(userId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to begin authenticator setup for user {UserId}.", TryGetCurrentUserId());
                return BadRequest(new { error = "Unable to start authenticator setup. Please try again." });
            }
        }

        [HttpPost("mfa/authenticator/reset")]
        [Authorize]
        [EnableRateLimiting(AuthRateLimitPolicyNames.MfaManage)]
        public async Task<IActionResult> ResetAuthenticator([FromBody] MfaReauthenticationDTO dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                var response = await _authService.ResetAuthenticatorAsync(userId, dto);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset authenticator for user {UserId}.", TryGetCurrentUserId());
                return BadRequest(new { error = "Unable to reset authenticator. Please verify your credentials and try again." });
            }
        }

        [HttpPost("mfa/authenticator/verify")]
        [Authorize]
        [EnableRateLimiting(AuthRateLimitPolicyNames.MfaManage)]
        public async Task<IActionResult> VerifyAuthenticatorSetup([FromBody] VerifyAuthenticatorSetupDTO dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                var response = await _authService.VerifyAuthenticatorSetupAsync(userId, dto);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify authenticator setup for user {UserId}.", TryGetCurrentUserId());
                return BadRequest(new { error = "Unable to verify authenticator setup. Please check your code and try again." });
            }
        }

        [HttpPost("mfa/recovery-codes/regenerate")]
        [Authorize]
        [EnableRateLimiting(AuthRateLimitPolicyNames.MfaManage)]
        public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] MfaReauthenticationDTO dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                var response = await _authService.RegenerateRecoveryCodesAsync(userId, dto);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to regenerate MFA recovery codes for user {UserId}.", TryGetCurrentUserId());
                return BadRequest(new { error = "Unable to regenerate recovery codes. Please verify your credentials and try again." });
            }
        }

        [HttpPost("mfa/disable")]
        [Authorize]
        [EnableRateLimiting(AuthRateLimitPolicyNames.MfaManage)]
        public async Task<IActionResult> DisableMfa([FromBody] MfaReauthenticationDTO dto)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _authService.DisableMfaAsync(userId, dto);
                return Ok(new { message = "Two-factor authentication has been disabled." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to disable MFA for user {UserId}.", TryGetCurrentUserId());
                return BadRequest(new { error = "Unable to disable MFA. Please verify your credentials and try again." });
            }
        }

        private int? TryGetCurrentUserId()
        {
            var userIdClaim = User.FindFirst("UserId");
            return userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId)
                ? userId
                : null;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var userId))
            {
                return userId;
            }

            throw new UnauthorizedAccessException("User ID not found in token.");
        }
    }
}
