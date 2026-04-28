using AccountingSystem.API.Security;

namespace AccountingSystem.API.Services.Interfaces
{
    public interface IEmailOtpChallengeStore
    {
        EmailOtpIssueResult Issue(
            string challengeKey,
            int identityUserId,
            int legacyUserId,
            string code,
            TimeSpan expiresAfter,
            TimeSpan resendCooldown);

        EmailOtpVerificationResult Verify(
            string challengeKey,
            int identityUserId,
            int legacyUserId,
            string code,
            int maxAttempts);
    }
}
