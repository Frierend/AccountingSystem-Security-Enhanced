using AccountingSystem.Client.Services;
using FluentAssertions;

namespace AccountingSystem.Client.Tests;

public class AuditTimestampFormatterTests
{
    [Fact]
    public void FormatLocalDateTime_WhenTimestampIsUtc_ShouldMatchTenantAuditLogStyle()
    {
        var timestamp = new DateTime(2026, 5, 5, 8, 30, 0, DateTimeKind.Utc);

        var formatted = AuditTimestampFormatter.FormatLocalDateTime(timestamp);

        formatted.Should().Be(timestamp.ToLocalTime().ToString("MMM dd, yyyy HH:mm"));
        formatted.Should().NotContain(":00");
    }
}
