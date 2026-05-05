using System.Security.Claims;
using AccountingSystem.API.Controllers;
using AccountingSystem.API.Data;
using AccountingSystem.API.Identity;
using AccountingSystem.API.Models;
using AccountingSystem.API.Services.Interfaces;
using AccountingSystem.Shared.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace AccountingSystem.API.Tests;

public class SuperAdminControllerTests
{
    private const string ActorPassword = "LongPassword123!";
    private const string DefaultReason = "Backup governance continuity coverage.";

    [Fact]
    public async Task CreateSuperAdmin_RequiresStepUpPayload()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "primary-admin@test.com");
        var controller = CreateController(context, harness, actor.Id);

        var result = await controller.CreateSuperAdmin(new CreateSuperAdminRequestDTO
        {
            SuperAdmin = BuildCreatePayload("backup-admin@test.com"),
            StepUp = null!
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateSuperAdmin_WhenCurrentPasswordIsWrong_ShouldFailSafely()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "primary-admin@test.com");
        var controller = CreateController(context, harness, actor.Id);

        var result = await controller.CreateSuperAdmin(new CreateSuperAdminRequestDTO
        {
            SuperAdmin = BuildCreatePayload("backup-admin@test.com"),
            StepUp = BuildStepUpPayload(currentPassword: "WrongPassword123!", reason: DefaultReason)
        });

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        (await context.Users.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        var failedLog = await context.SuperAdminAuditLogs.IgnoreQueryFilters()
            .SingleAsync(log => log.Action == "SUPERADMIN-STEPUP-FAILED");
        failedLog.Details.Should().Contain("InvalidCurrentPassword");
    }

    [Fact]
    public async Task CreateSuperAdmin_WithoutReason_ShouldFail()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "primary-admin@test.com");
        var controller = CreateController(context, harness, actor.Id);

        var result = await controller.CreateSuperAdmin(new CreateSuperAdminRequestDTO
        {
            SuperAdmin = BuildCreatePayload("backup-admin@test.com"),
            StepUp = BuildStepUpPayload(currentPassword: ActorPassword, reason: "  ")
        });

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task CreateSuperAdmin_WithValidStepUp_ShouldCreateBackupAccountAndAuditReason()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "primary-admin@test.com");
        var controller = CreateController(context, harness, actor.Id);
        const string reason = "Create backup SuperAdmin for incident recovery readiness.";

        var result = await controller.CreateSuperAdmin(new CreateSuperAdminRequestDTO
        {
            SuperAdmin = BuildCreatePayload("backup-admin@test.com"),
            StepUp = BuildStepUpPayload(currentPassword: ActorPassword, reason: reason)
        });

        result.Should().BeOfType<OkObjectResult>();

        var backupUser = await context.Users
            .IgnoreQueryFilters()
            .Include(user => user.Role)
            .SingleAsync(user => user.Email == "backup-admin@test.com");
        backupUser.Role.Name.Should().Be("SuperAdmin");
        backupUser.Status.Should().Be("Active");

        var createAudit = await context.SuperAdminAuditLogs.IgnoreQueryFilters()
            .SingleAsync(log => log.Action == "SUPERADMIN-CREATE");
        createAudit.Details.Should().Contain(reason);
        createAudit.Details.Should().Contain("StepUpMethod=PasswordOnly");
    }

    [Fact]
    public async Task CreateSuperAdmin_WhenActorHasMfaEnabled_ShouldRequireValidMfa()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "primary-admin@test.com");
        var controller = CreateController(context, harness, actor.Id);

        await EnableAuthenticatorMfaAsync(harness, actor.Id);

        var missingMfaResult = await controller.CreateSuperAdmin(new CreateSuperAdminRequestDTO
        {
            SuperAdmin = BuildCreatePayload("backup-admin@test.com"),
            StepUp = BuildStepUpPayload(currentPassword: ActorPassword, reason: DefaultReason)
        });

        missingMfaResult.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task UpdateSuperAdminStatus_RequiresStepUpPayload()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "actor@test.com");
        var target = await SeedSuperAdminAsync(context, harness, "target@test.com");
        var controller = CreateController(context, harness, actor.Id, actor.Email);

        var result = await controller.UpdateSuperAdminStatus(target.Id, new UpdateSuperAdminStatusRequestDTO
        {
            Status = "Blocked",
            StepUp = null!
        });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateSuperAdminStatus_WithWrongPassword_ShouldFail()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "actor@test.com");
        var target = await SeedSuperAdminAsync(context, harness, "target@test.com");
        var controller = CreateController(context, harness, actor.Id, actor.Email);

        var result = await controller.UpdateSuperAdminStatus(target.Id, new UpdateSuperAdminStatusRequestDTO
        {
            Status = "Blocked",
            StepUp = BuildStepUpPayload("WrongPassword123!", DefaultReason)
        });

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);

        var reloaded = await context.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == target.Id);
        reloaded.Status.Should().Be("Active");
    }

    [Fact]
    public async Task UpdateSuperAdminStatus_WithoutReason_ShouldFail()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "actor@test.com");
        var target = await SeedSuperAdminAsync(context, harness, "target@test.com");
        var controller = CreateController(context, harness, actor.Id, actor.Email);

        var result = await controller.UpdateSuperAdminStatus(target.Id, new UpdateSuperAdminStatusRequestDTO
        {
            Status = "Blocked",
            StepUp = BuildStepUpPayload(ActorPassword, " ")
        });

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UpdateSuperAdminStatus_WithValidStepUp_ShouldSucceed()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "actor@test.com");
        var target = await SeedSuperAdminAsync(context, harness, "target@test.com");
        var controller = CreateController(context, harness, actor.Id, actor.Email);
        const string reason = "Disable account pending security review.";

        var result = await controller.UpdateSuperAdminStatus(target.Id, new UpdateSuperAdminStatusRequestDTO
        {
            Status = "Blocked",
            StepUp = BuildStepUpPayload(ActorPassword, reason)
        });

        result.Should().BeOfType<OkObjectResult>();

        var reloaded = await context.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == target.Id);
        reloaded.Status.Should().Be("Blocked");
        reloaded.IsActive.Should().BeFalse();

        var auditLog = await context.SuperAdminAuditLogs.IgnoreQueryFilters()
            .SingleAsync(log => log.Action == "SUPERADMIN-DISABLE");
        auditLog.Details.Should().Contain(reason);
    }

    [Fact]
    public async Task UpdateSuperAdminStatus_WhenLastActiveSuperAdminWouldBeBlocked_ShouldRejectAndAuditProtection()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var target = await SeedSuperAdminAsync(context, harness, "primary-admin@test.com");
        var actor = await SeedSuperAdminAsync(context, harness, "actor@test.com");
        var controller = CreateController(context, harness, actor.Id, actor.Email);

        actor.Status = "Blocked";
        actor.IsActive = false;
        await context.SaveChangesAsync();

        var result = await controller.UpdateSuperAdminStatus(target.Id, new UpdateSuperAdminStatusRequestDTO
        {
            Status = "Blocked",
            StepUp = BuildStepUpPayload(ActorPassword, "Attempt block final admin.")
        });

        result.Should().BeOfType<BadRequestObjectResult>();

        var reloadedTarget = await context.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == target.Id);
        reloadedTarget.Status.Should().Be("Active");
        reloadedTarget.IsActive.Should().BeTrue();

        context.SuperAdminAuditLogs.IgnoreQueryFilters()
            .Any(log => log.Action == "SUPERADMIN-LAST-ADMIN-PROTECTION")
            .Should().BeTrue();
    }

    [Fact]
    public async Task NonSuperAdminActor_CannotCreateOrDisableSuperAdmin()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var target = await SeedSuperAdminAsync(context, harness, "target@test.com");
        var controller = CreateController(context, harness, currentUserId: 999, currentUserEmail: "tenant-admin@test.com", role: "Admin");

        var createResult = await controller.CreateSuperAdmin(new CreateSuperAdminRequestDTO
        {
            SuperAdmin = BuildCreatePayload("backup-admin@test.com"),
            StepUp = BuildStepUpPayload(ActorPassword, DefaultReason)
        });

        var disableResult = await controller.UpdateSuperAdminStatus(target.Id, new UpdateSuperAdminStatusRequestDTO
        {
            Status = "Blocked",
            StepUp = BuildStepUpPayload(ActorPassword, DefaultReason)
        });

        createResult.Should().BeOfType<ForbidResult>();
        disableResult.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task SuperAdminAuditLogs_ShouldExcludeSensitiveStepUpDataAndIncludeSafeReason()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var actor = await SeedSuperAdminAsync(context, harness, "primary-admin@test.com");
        var controller = CreateController(context, harness, actor.Id);
        var identityUser = await EnableAuthenticatorMfaAsync(harness, actor.Id);
        var authenticatorKey = await harness.UserManager.GetAuthenticatorKeyAsync(identityUser);
        var mfaCode = TestHelpers.GenerateAuthenticatorCode(authenticatorKey!);
        var recoveryCodeProbe = "RECOVERY-SHOULD-NOT-LOG";
        const string reason = "Provision additional trusted SuperAdmin for emergency operations.";

        var result = await controller.CreateSuperAdmin(new CreateSuperAdminRequestDTO
        {
            SuperAdmin = BuildCreatePayload("backup-admin@test.com"),
            StepUp = new SuperAdminStepUpVerificationDTO
            {
                CurrentPassword = ActorPassword,
                MfaMethod = MfaLoginMethods.AuthenticatorApp,
                MfaCode = mfaCode,
                RecoveryCode = recoveryCodeProbe,
                Reason = reason
            }
        });

        result.Should().BeOfType<OkObjectResult>();

        var allDetails = string.Join(
            Environment.NewLine,
            await context.SuperAdminAuditLogs.IgnoreQueryFilters().Select(log => log.Details).ToListAsync());

        allDetails.Should().Contain(reason);
        allDetails.Should().NotContain(ActorPassword);
        allDetails.Should().NotContain(mfaCode);
        allDetails.Should().NotContain(recoveryCodeProbe);
        allDetails.Should().NotContain("captcha");
        allDetails.ToLowerInvariant().Should().NotContain("jwt");
        allDetails.ToLowerInvariant().Should().NotContain("token");
    }

    private static CreateSuperAdminDTO BuildCreatePayload(string email)
    {
        return new CreateSuperAdminDTO
        {
            FullName = "Backup Admin",
            Email = email,
            Password = "BackupPassword123!",
            ConfirmPassword = "BackupPassword123!"
        };
    }

    private static SuperAdminStepUpVerificationDTO BuildStepUpPayload(string currentPassword, string reason)
    {
        return new SuperAdminStepUpVerificationDTO
        {
            CurrentPassword = currentPassword,
            Reason = reason
        };
    }

    private static SuperAdminController CreateController(
        AccountingDbContext context,
        IdentityTestHarness harness,
        int currentUserId,
        string currentUserEmail = "primary-admin@test.com",
        string role = "SuperAdmin")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppUrls:ClientBaseUrl"] = "https://client.example.test",
                ["Mfa:EmailOtpExpirationMinutes"] = "5",
                ["Mfa:EmailOtpMaxVerificationAttempts"] = "3",
                ["Mfa:EmailOtpResendCooldownSeconds"] = "60"
            })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("UserId", currentUserId.ToString()),
            new Claim("unique_name", currentUserEmail),
            new Claim(ClaimTypes.Role, role)
        }, "Test"));
        httpContext.Request.Headers.Origin = "https://client.example.test";

        return new SuperAdminController(
            context,
            Mock.Of<ILogger<SuperAdminController>>(),
            Mock.Of<ILegacyIdentityBridgeService>(),
            harness.IdentityContext,
            harness.AccountService,
            harness.EmailService,
            new AccountingSystem.API.Services.EmailOtpChallengeStore(),
            harness.UserManager,
            configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static async Task<User> SeedSuperAdminAsync(
        AccountingDbContext context,
        IdentityTestHarness harness,
        string email)
    {
        var role = await EnsureRoleAsync(context);
        var company = await EnsureHostCompanyAsync(context);

        var superAdmin = TestHelpers.CreateUser(role, company.Id, email, ActorPassword);
        context.Users.Add(superAdmin);
        await context.SaveChangesAsync();

        await harness.AccountService.EnsureProvisionedAsync(
            TestHelpers.CreateIdentitySnapshot(
                superAdmin,
                role.Name,
                requireEmailConfirmation: true,
                emailConfirmed: true),
            ActorPassword);

        return superAdmin;
    }

    private static async Task<Role> EnsureRoleAsync(AccountingDbContext context)
    {
        var role = await context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Name == "SuperAdmin");
        if (role != null)
        {
            return role;
        }

        role = new Role { Name = "SuperAdmin" };
        context.Roles.Add(role);
        await context.SaveChangesAsync();
        return role;
    }

    private static async Task<Company> EnsureHostCompanyAsync(AccountingDbContext context)
    {
        var company = await context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Name == "SaaS Operations");
        if (company != null)
        {
            return company;
        }

        company = new Company
        {
            Name = "SaaS Operations",
            IsActive = true,
            Status = "Active"
        };
        context.Companies.Add(company);
        await context.SaveChangesAsync();
        return company;
    }

    private static async Task<ApplicationUser> EnableAuthenticatorMfaAsync(IdentityTestHarness harness, int legacyUserId)
    {
        var identityUser = await harness.IdentityContext.Users.SingleAsync(user => user.LegacyUserId == legacyUserId);
        (await harness.UserManager.ResetAuthenticatorKeyAsync(identityUser)).Succeeded.Should().BeTrue();
        (await harness.UserManager.SetTwoFactorEnabledAsync(identityUser, true)).Succeeded.Should().BeTrue();
        return identityUser;
    }
}
