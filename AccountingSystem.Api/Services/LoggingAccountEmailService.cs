using AccountingSystem.API.Services.Interfaces;

namespace AccountingSystem.API.Services
{
    public class LoggingAccountEmailService : IAccountEmailService
    {
        private readonly ILogger<LoggingAccountEmailService> _logger;

        public LoggingAccountEmailService(ILogger<LoggingAccountEmailService> logger)
        {
            _logger = logger;
        }

        public Task SendPasswordResetAsync(string email, string fullName, string resetLink, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Development email sender: password reset link for {Email} ({FullName}): {ResetLink}",
                email,
                fullName,
                resetLink);

            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(string email, string fullName, string confirmationLink, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Development email sender: email confirmation link for {Email} ({FullName}): {ConfirmationLink}",
                email,
                fullName,
                confirmationLink);

            return Task.CompletedTask;
        }

        public Task SendEmailOtpAsync(string email, string fullName, string otpCode, int expiresInMinutes, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Development email sender: email OTP requested for {Email} ({FullName}). ExpiresInMinutes={ExpiresInMinutes}.",
                email,
                fullName,
                expiresInMinutes);

            return Task.CompletedTask;
        }
    }
}
