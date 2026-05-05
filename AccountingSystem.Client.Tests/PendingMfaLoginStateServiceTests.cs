using AccountingSystem.Client.Services;
using AccountingSystem.Shared.DTOs;
using FluentAssertions;

namespace AccountingSystem.Client.Tests;

public class PendingMfaLoginStateServiceTests
{
    [Fact]
    public void SupportsMethod_WhenRecoveryCodeIsNotAdvertised_ShouldNotExposeRecoveryCode()
    {
        var state = new PendingMfaLoginStateService();
        state.Set(
            "challenge",
            "user@example.com",
            new List<string> { MfaLoginMethods.AuthenticatorApp },
            MfaLoginMethods.AuthenticatorApp,
            emailOtpSent: false);

        state.SupportsMethod(MfaLoginMethods.AuthenticatorApp).Should().BeTrue();
        state.SupportsMethod(MfaLoginMethods.RecoveryCode).Should().BeFalse();
        state.SupportsMethod(MfaLoginMethods.EmailOtp).Should().BeFalse();
    }

    [Fact]
    public void SupportsMethod_WhenOnlyEmailOtpIsAdvertised_ShouldDefaultToEmailOtpOnly()
    {
        var state = new PendingMfaLoginStateService();
        state.Set(
            "challenge",
            "user@example.com",
            new List<string> { MfaLoginMethods.EmailOtp },
            MfaLoginMethods.EmailOtp,
            emailOtpSent: true);

        state.SupportsMethod(MfaLoginMethods.EmailOtp).Should().BeTrue();
        state.SupportsMethod(MfaLoginMethods.AuthenticatorApp).Should().BeFalse();
        state.SupportsMethod(MfaLoginMethods.RecoveryCode).Should().BeFalse();
        state.EmailOtpSent.Should().BeTrue();
    }
}
