namespace AccountingSystem.Client.Services
{
    public sealed class LoginRequiresRecaptchaException : Exception
    {
        public LoginRequiresRecaptchaException(string message)
            : base(message)
        {
        }
    }
}
