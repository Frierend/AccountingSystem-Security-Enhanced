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
    [Fact]
    public async Task CreateSuperAdmin_WhenValid_ShouldCreateBackupAccountAndAuditEvent()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var controller = CreateController(context, harness, currentUserId: 1);

        await SeedHostSuperAdminAsync(context, currentUserId: 1);

        var result = await controller.CreateSuperAdmin(new CreateSuperAdminDTO
        {
            FullName = "Backup Admin",
            Email = "backup-admin@test.com",
            Password = "LongPassword123!",
            ConfirmPassword = "LongPassword123!"
        });

        result.Should().BeOfType<OkObjectResult>();

        var backupUser = await context.Users
            .IgnoreQueryFilters()
            .Include(user => user.Role)
            .SingleAsync(user => user.Email == "backup-admin@test.com");
        backupUser.Role.Name.Should().Be("SuperAdmin");
        backupUser.Status.Should().Be("Active");

        var identityUser = await harness.IdentityContext.Users.SingleAsync(user => user.LegacyUserId == backupUser.Id);
        identityUser.EmailConfirmed.Should().BeFalse();
        identityUser.RequireEmailConfirmation.Should().BeTrue();
        harness.EmailService.SentConfirmationEmails.Should().ContainSingle(email => email.Email == "backup-admin@test.com");

        var auditLog = await context.SuperAdminAuditLogs.IgnoreQueryFilters().SingleAsync(log => log.Action == "SUPERADMIN-CREATE");
        auditLog.TargetType.Should().Be("SuperAdminAccount");
        auditLog.Details.Should().NotContain("LongPassword123!");
    }

    [Fact]
    public async Task UpdateSuperAdminStatus_WhenLastActiveSuperAdminWouldBeBlocked_ShouldRejectAndAuditProtection()
    {
        var context = TestHelpers.CreateContext();
        using var harness = TestHelpers.CreateIdentityHarness();
        var controller = CreateController(context, harness, currentUserId: 99);

        var superAdmin = await SeedHostSuperAdminAsync(context, currentUserId: 1);

        var result = await controller.UpdateSuperAdminStatus(
            superAdmin.Id,
            new UpdateUserStatusDTO { Status = "Blocked" });

        result.Should().BeOfType<BadRequestObjectResult>();

        var reloadedSuperAdmin = await context.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == superAdmin.Id);
        reloadedSuperAdmin.Status.Should().Be("Active");
        reloadedSuperAdmin.IsActive.Should().BeTrue();

        var auditLog = await context.SuperAdminAuditLogs.IgnoreQueryFilters().SingleAsync();
        auditLog.Action.Should().Be("SUPERADMIN-LAST-ADMIN-PROTECTION");
        auditLog.TargetType.Should().Be("SuperAdminAccount");
    }

    private static SuperAdminController CreateController(
        AccountingDbContext context,
        IdentityTestHarness harness,
        int currentUserId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppUrls:ClientBaseUrl"] = "https://client.example.test"
            })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("UserId", currentUserId.ToString()),
            new Claim("unique_name", "primary-admin@test.com"),
            new Claim(ClaimTypes.Role, "SuperAdmin")
        }, "Test"));
        httpContext.Request.Headers.Origin = "https://client.example.test";

        return new SuperAdminController(
            context,
            Mock.Of<ILogger<SuperAdminController>>(),
            Mock.Of<ILegacyIdentityBridgeService>(),
            harness.IdentityContext,
            harness.AccountService,
            harness.EmailService,
            harness.UserManager,
            configuration)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static async Task<User> SeedHostSuperAdminAsync(AccountingDbContext context, int currentUserId)
    {
        var role = new Role { Id = 4, Name = "SuperAdmin" };
        var company = new Company { Id = 1, Name = "SaaS Operations", IsActive = true, Status = "Active" };
        var superAdmin = TestHelpers.CreateUser(
            role,
            company.Id,
            currentUserId == 1 ? "primary-admin@test.com" : $"primary-admin-{currentUserId}@test.com",
            "LongPassword123!");

        context.Roles.Add(role);
        context.Companies.Add(company);
        context.Users.Add(superAdmin);
        await context.SaveChangesAsync();

        return superAdmin;
    }
}
