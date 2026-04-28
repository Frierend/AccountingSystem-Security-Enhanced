namespace AccountingSystem.API.Configuration
{
    public class MfaSettings
    {
        public string AuthenticatorIssuer { get; set; } = "AccountingSystem";

        public int LoginChallengeLifespanMinutes { get; set; } = 5;

        public int EmailOtpExpirationMinutes { get; set; } = 5;

        public int EmailOtpMaxVerificationAttempts { get; set; } = 3;

        public int EmailOtpResendCooldownSeconds { get; set; } = 60;
    }
}
