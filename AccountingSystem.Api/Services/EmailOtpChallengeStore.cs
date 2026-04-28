using AccountingSystem.API.Security;
using AccountingSystem.API.Services.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace AccountingSystem.API.Services
{
    public class EmailOtpChallengeStore : IEmailOtpChallengeStore
    {
        private readonly Dictionary<string, EmailOtpChallenge> _challenges = new(StringComparer.Ordinal);
        private readonly object _syncRoot = new();

        public EmailOtpIssueResult Issue(
            string challengeKey,
            int identityUserId,
            int legacyUserId,
            string code,
            TimeSpan expiresAfter,
            TimeSpan resendCooldown)
        {
            var now = DateTime.UtcNow;

            lock (_syncRoot)
            {
                if (_challenges.TryGetValue(challengeKey, out var existing))
                {
                    if (existing.ExpiresAtUtc <= now)
                    {
                        _challenges.Remove(challengeKey);
                    }
                    else
                    {
                        var retryAtUtc = existing.IssuedAtUtc.Add(resendCooldown);
                        if (retryAtUtc > now)
                        {
                            return new EmailOtpIssueResult(
                                EmailOtpIssueStatus.RateLimited,
                                existing.ExpiresAtUtc,
                                retryAtUtc - now);
                        }
                    }
                }

                var salt = RandomNumberGenerator.GetBytes(32);
                var challenge = new EmailOtpChallenge(
                    identityUserId,
                    legacyUserId,
                    HashCode(code, salt, identityUserId, legacyUserId, challengeKey),
                    salt,
                    now,
                    now.Add(expiresAfter));

                _challenges[challengeKey] = challenge;

                return new EmailOtpIssueResult(EmailOtpIssueStatus.Issued, challenge.ExpiresAtUtc);
            }
        }

        public EmailOtpVerificationResult Verify(
            string challengeKey,
            int identityUserId,
            int legacyUserId,
            string code,
            int maxAttempts)
        {
            var now = DateTime.UtcNow;

            lock (_syncRoot)
            {
                if (!_challenges.TryGetValue(challengeKey, out var challenge) ||
                    challenge.IdentityUserId != identityUserId ||
                    challenge.LegacyUserId != legacyUserId)
                {
                    return new EmailOtpVerificationResult(EmailOtpVerificationStatus.NotFound, 0);
                }

                if (challenge.ExpiresAtUtc <= now)
                {
                    _challenges.Remove(challengeKey);
                    return new EmailOtpVerificationResult(EmailOtpVerificationStatus.Expired, challenge.FailedAttempts);
                }

                if (challenge.FailedAttempts >= maxAttempts)
                {
                    _challenges.Remove(challengeKey);
                    return new EmailOtpVerificationResult(EmailOtpVerificationStatus.TooManyAttempts, challenge.FailedAttempts);
                }

                var normalizedCode = NormalizeCode(code);
                var candidateHash = HashCode(normalizedCode, challenge.Salt, identityUserId, legacyUserId, challengeKey);
                if (CryptographicOperations.FixedTimeEquals(candidateHash, challenge.CodeHash))
                {
                    _challenges.Remove(challengeKey);
                    return new EmailOtpVerificationResult(EmailOtpVerificationStatus.Success, challenge.FailedAttempts);
                }

                challenge.FailedAttempts++;
                if (challenge.FailedAttempts >= maxAttempts)
                {
                    _challenges.Remove(challengeKey);
                    return new EmailOtpVerificationResult(EmailOtpVerificationStatus.TooManyAttempts, challenge.FailedAttempts);
                }

                return new EmailOtpVerificationResult(EmailOtpVerificationStatus.Invalid, challenge.FailedAttempts);
            }
        }

        private static byte[] HashCode(
            string code,
            byte[] salt,
            int identityUserId,
            int legacyUserId,
            string challengeKey)
        {
            var normalizedCode = NormalizeCode(code);
            var codeBytes = Encoding.UTF8.GetBytes($"{identityUserId}:{legacyUserId}:{challengeKey}:{normalizedCode}");
            using var hmac = new HMACSHA256(salt);
            return hmac.ComputeHash(codeBytes);
        }

        private static string NormalizeCode(string code)
        {
            return new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private sealed class EmailOtpChallenge
        {
            public EmailOtpChallenge(
                int identityUserId,
                int legacyUserId,
                byte[] codeHash,
                byte[] salt,
                DateTime issuedAtUtc,
                DateTime expiresAtUtc)
            {
                IdentityUserId = identityUserId;
                LegacyUserId = legacyUserId;
                CodeHash = codeHash;
                Salt = salt;
                IssuedAtUtc = issuedAtUtc;
                ExpiresAtUtc = expiresAtUtc;
            }

            public int IdentityUserId { get; }

            public int LegacyUserId { get; }

            public byte[] CodeHash { get; }

            public byte[] Salt { get; }

            public DateTime IssuedAtUtc { get; }

            public DateTime ExpiresAtUtc { get; }

            public int FailedAttempts { get; set; }
        }
    }
}
