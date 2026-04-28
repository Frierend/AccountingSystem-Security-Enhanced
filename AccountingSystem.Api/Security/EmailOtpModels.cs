namespace AccountingSystem.API.Security
{
    public enum EmailOtpIssueStatus
    {
        Issued,
        RateLimited
    }

    public enum EmailOtpVerificationStatus
    {
        Success,
        NotFound,
        Invalid,
        Expired,
        TooManyAttempts
    }

    public sealed record EmailOtpIssueResult(
        EmailOtpIssueStatus Status,
        DateTime ExpiresAtUtc,
        TimeSpan? RetryAfter = null)
    {
        public bool Succeeded => Status == EmailOtpIssueStatus.Issued;
    }

    public sealed record EmailOtpVerificationResult(
        EmailOtpVerificationStatus Status,
        int FailedAttempts)
    {
        public bool Succeeded => Status == EmailOtpVerificationStatus.Success;
    }
}
