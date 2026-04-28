namespace AccountingSystem.Client.Services
{
    public class PendingMfaLoginStateService
    {
        public string ChallengeToken { get; private set; } = string.Empty;

        public string Email { get; private set; } = string.Empty;

        public List<string> AvailableMethods { get; private set; } = new();

        public string PreferredMethod { get; private set; } = string.Empty;

        public bool EmailOtpSent { get; private set; }

        public bool HasPendingChallenge => !string.IsNullOrWhiteSpace(ChallengeToken);

        public void Set(string challengeToken, string email)
        {
            Set(challengeToken, email, new List<string>(), string.Empty, emailOtpSent: false);
        }

        public void Set(
            string challengeToken,
            string email,
            List<string>? availableMethods,
            string preferredMethod,
            bool emailOtpSent)
        {
            ChallengeToken = challengeToken;
            Email = email;
            AvailableMethods = availableMethods ?? new List<string>();
            PreferredMethod = preferredMethod;
            EmailOtpSent = emailOtpSent;
        }

        public bool SupportsMethod(string method)
        {
            return AvailableMethods.Count == 0 ||
                   AvailableMethods.Any(value => string.Equals(value, method, StringComparison.OrdinalIgnoreCase));
        }

        public void MarkEmailOtpSent()
        {
            EmailOtpSent = true;
        }

        public void Clear()
        {
            ChallengeToken = string.Empty;
            Email = string.Empty;
            AvailableMethods = new List<string>();
            PreferredMethod = string.Empty;
            EmailOtpSent = false;
        }
    }
}
