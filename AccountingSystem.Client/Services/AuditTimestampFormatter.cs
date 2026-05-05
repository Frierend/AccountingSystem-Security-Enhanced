namespace AccountingSystem.Client.Services
{
    public static class AuditTimestampFormatter
    {
        public static string FormatLocalDateTime(DateTime timestamp)
        {
            return timestamp.ToLocalTime().ToString("MMM dd, yyyy HH:mm");
        }
    }
}
