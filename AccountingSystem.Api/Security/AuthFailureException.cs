namespace AccountingSystem.API.Security
{
    internal sealed class AuthFailureException : Exception
    {
        internal const string DefaultPublicMessage = "Invalid email or password. Please try again later.";
        internal const string LockoutPublicMessage = "Your account is temporarily locked due to repeated failed login attempts. Please try again later.";

        internal AuthFailureException(
            string internalReason,
            string publicMessage = DefaultPublicMessage,
            int statusCode = StatusCodes.Status401Unauthorized,
            bool requiresRecaptcha = false)
            : base(publicMessage)
        {
            InternalReason = internalReason;
            PublicMessage = publicMessage;
            StatusCode = statusCode;
            RequiresRecaptcha = requiresRecaptcha;
        }

        internal string InternalReason { get; }

        internal string PublicMessage { get; }

        internal int StatusCode { get; }

        internal bool RequiresRecaptcha { get; }
    }
}
