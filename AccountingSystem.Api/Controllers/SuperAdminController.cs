using AccountingSystem.API.Configuration;
using AccountingSystem.API.Data;
using AccountingSystem.API.Identity;
using AccountingSystem.API.Models;
using AccountingSystem.API.Services.Interfaces;
using AccountingSystem.Shared.DTOs;
using AccountingSystem.Shared.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Transactions;

namespace AccountingSystem.API.Controllers
{
    [ApiController]
    [Route("api/superadmin")]
    [Authorize(Roles = "SuperAdmin")] // STRICTLY RESTRICTED
    public class SuperAdminController : ControllerBase
    {
        private readonly AccountingDbContext _context;
        private readonly IdentityAuthDbContext _identityContext;
        private readonly ILogger<SuperAdminController> _logger;
        private readonly ILegacyIdentityBridgeService _identityBridgeService;
        private readonly IIdentityAccountService _identityAccountService;
        private readonly IAccountEmailService _accountEmailService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;

        public SuperAdminController(
            AccountingDbContext context,
            ILogger<SuperAdminController> logger,
            ILegacyIdentityBridgeService identityBridgeService,
            IdentityAuthDbContext identityContext,
            IIdentityAccountService identityAccountService,
            IAccountEmailService accountEmailService,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration)
        {
            _context = context;
            _identityContext = identityContext;
            _logger = logger;
            _identityBridgeService = identityBridgeService;
            _identityAccountService = identityAccountService;
            _accountEmailService = accountEmailService;
            _userManager = userManager;
            _configuration = configuration;
        }

        private int GetCurrentUserId() => int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        private string GetCurrentUserEmail() => User.FindFirst("unique_name")?.Value ?? User.Identity?.Name ?? "Unknown";

        private async Task<int> GetHostCompanyId()
        {
            var host = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Name == "SaaS Operations");
            return host?.Id ?? 0;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboardStats()
        {
            var hostCompanyId = await GetHostCompanyId();

            var companies = await _context.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Id != hostCompanyId)
                .ToListAsync();

            var users = await _context.Users
                .IgnoreQueryFilters()
                .Include(u => u.Role)
                .Where(u => u.Role.Name != "SuperAdmin" && !u.IsDeleted)
                .ToListAsync();

            var now = DateTime.UtcNow;
            var twelveMonthsAgo = now.AddMonths(-11).Date;
            twelveMonthsAgo = new DateTime(twelveMonthsAgo.Year, twelveMonthsAgo.Month, 1);

            var monthlyRegistrations = companies
                .Where(c => c.CreatedAt >= twelveMonthsAgo)
                .GroupBy(c => new { c.CreatedAt.Year, c.CreatedAt.Month })
                .Select(g => new MonthlyActivityDTO
                {
                    Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                    Count = g.Count()
                })
                .OrderBy(m => m.Month)
                .ToList();

            var allMonths = Enumerable.Range(0, 12).Select(i =>
            {
                var date = twelveMonthsAgo.AddMonths(i);
                return $"{date.Year}-{date.Month:D2}";
            }).ToList();

            monthlyRegistrations = allMonths.Select(m =>
                monthlyRegistrations.FirstOrDefault(r => r.Month == m) ?? new MonthlyActivityDTO { Month = m, Count = 0 }
            ).ToList();

            var monthlyUserGrowth = users
                .Where(u => u.CreatedAt >= twelveMonthsAgo)
                .GroupBy(u => new { u.CreatedAt.Year, u.CreatedAt.Month })
                .Select(g => new MonthlyActivityDTO
                {
                    Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                    Count = g.Count()
                })
                .OrderBy(m => m.Month)
                .ToList();

            monthlyUserGrowth = allMonths.Select(m =>
                monthlyUserGrowth.FirstOrDefault(r => r.Month == m) ?? new MonthlyActivityDTO { Month = m, Count = 0 }
            ).ToList();

            _logger.LogInformation("Dashboard MonthlyRegistrations: {MonthlyRegistrations}",
                string.Join(", ", monthlyRegistrations.Select(m => $"{m.Month}:{m.Count}")));
            _logger.LogInformation("Dashboard MonthlyUserGrowth: {MonthlyUserGrowth}",
                string.Join(", ", monthlyUserGrowth.Select(m => $"{m.Month}:{m.Count}")));

            var recentActions = await _context.SuperAdminAuditLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(10)
                .Select(l => new SuperAdminAuditLogDTO
                {
                    Id = l.Id,
                    AdminEmail = l.AdminEmail,
                    Action = l.Action,
                    TargetType = l.TargetType,
                    TargetName = l.TargetName,
                    TargetId = l.TargetId,
                    OldValue = l.OldValue,
                    NewValue = l.NewValue,
                    Details = l.Details,
                    Timestamp = l.Timestamp
                })
                .ToListAsync();

            var stats = new SystemDashboardDTO
            {
                TotalCompanies = companies.Count,
                ActiveCompanies = companies.Count(c => c.Status == "Active" && c.IsActive),
                SuspendedCompanies = companies.Count(c => c.Status == "Suspended" || (!c.IsActive && c.Status != "Blocked")),
                BlockedCompanies = companies.Count(c => c.Status == "Blocked"),
                TotalUsers = users.Count,
                ActiveUsers = users.Count(u => u.Status == "Active" && u.IsActive),
                RestrictedUsers = users.Count(u => u.Status == "Restricted"),
                BlockedUsers = users.Count(u => u.Status == "Blocked"),
                MonthlyRegistrations = monthlyRegistrations,
                MonthlyUserGrowth = monthlyUserGrowth,
                RecentActions = recentActions
            };

            return Ok(stats);
        }

        [HttpGet("companies")]
        public async Task<IActionResult> GetAllCompanies()
        {
            var hostCompanyId = await GetHostCompanyId();

            var companies = await _context.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Id != hostCompanyId)
                .Select(c => new TenantDTO
                {
                    Id = c.Id,
                    Name = c.Name,
                    Address = c.Address,
                    CreatedAt = c.CreatedAt,
                    IsActive = c.IsActive,
                    Status = c.Status,
                    UserCount = _context.Users.IgnoreQueryFilters()
                        .Count(u => u.CompanyId == c.Id && u.Role.Name != "SuperAdmin" && !u.IsDeleted),
                    LastActivityDate = _context.AuditLogs.IgnoreQueryFilters()
                        .Where(a => a.CompanyId == c.Id)
                        .OrderByDescending(a => a.Timestamp)
                        .Select(a => (DateTime?)a.Timestamp)
                        .FirstOrDefault()
                })
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(companies);
        }

        [HttpPut("companies/{id}/status")]
        public async Task<IActionResult> UpdateCompanyStatus(int id, [FromBody] UpdateCompanyStatusDTO dto)
        {
            var company = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
            if (company == null) return NotFound("Company not found.");

            if (company.Name == "SaaS Operations")
                return BadRequest("Cannot modify the Host Operations company.");

            var validStatuses = new[] { "Active", "Suspended", "Blocked" };
            if (!validStatuses.Contains(dto.Status))
                return BadRequest("Invalid status. Must be: Active, Suspended, or Blocked.");

            var oldStatus = company.Status;
            company.Status = dto.Status;
            company.IsActive = dto.Status == "Active";

            await LogSuperAdminAction("COMPANY_STATUS_CHANGE", "Company", id, company.Name, oldStatus, dto.Status,
                $"Company '{company.Name}' status changed from {oldStatus} to {dto.Status}");

            await _context.SaveChangesAsync();

            return Ok(new { message = $"Company '{company.Name}' is now {dto.Status}." });
        }

        [HttpPut("companies/{id}/toggle")]
        public async Task<IActionResult> ToggleCompanyStatus(int id)
        {
            var company = await _context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
            if (company == null) return NotFound("Company not found.");

            if (company.Name == "SaaS Operations")
                return BadRequest("Cannot suspend the Host Operations company.");

            var oldStatus = company.Status;
            company.IsActive = !company.IsActive;
            company.Status = company.IsActive ? "Active" : "Suspended";

            await LogSuperAdminAction("COMPANY_STATUS_CHANGE", "Company", id, company.Name, oldStatus, company.Status,
                $"Company '{company.Name}' status toggled from {oldStatus} to {company.Status}");

            await _context.SaveChangesAsync();

            return Ok(new { message = company.IsActive ? "Company activated." : "Company suspended." });
        }

        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _context.Users
                .IgnoreQueryFilters()
                .Include(u => u.Role)
                .Where(u => u.Role.Name != "SuperAdmin" && !u.IsDeleted)
                .Join(_context.Companies.IgnoreQueryFilters(),
                    user => user.CompanyId,
                    company => company.Id,
                    (user, company) => new GlobalUserDTO
                    {
                        Id = user.Id,
                        FullName = user.FullName,
                        Email = user.Email,
                        Role = user.Role.Name,
                        CompanyName = company.Name,
                        CompanyId = company.Id,
                        IsActive = user.IsActive,
                        Status = user.Status,
                        CreatedAt = user.CreatedAt
                    })
                .OrderBy(u => u.CompanyName)
                .ThenBy(u => u.FullName)
                .ToListAsync();

            return Ok(users);
        }

        [HttpGet("superadmins")]
        public async Task<IActionResult> GetSuperAdminAccounts()
        {
            var superAdminRole = await GetSuperAdminRoleAsync();
            var currentUserId = GetCurrentUserId();

            var accounts = await _context.Users
                .IgnoreQueryFilters()
                .Where(user => user.RoleId == superAdminRole.Id && !user.IsDeleted)
                .OrderByDescending(user => user.IsActive && user.Status == "Active")
                .ThenBy(user => user.FullName)
                .ToListAsync();

            var legacyIds = accounts.Select(account => account.Id).ToList();
            var identityUsers = await _identityContext.Users
                .AsNoTracking()
                .Where(user => user.LegacyUserId.HasValue && legacyIds.Contains(user.LegacyUserId.Value))
                .ToDictionaryAsync(user => user.LegacyUserId!.Value);

            var response = accounts.Select(account =>
            {
                identityUsers.TryGetValue(account.Id, out var identityUser);
                return new SuperAdminAccountDTO
                {
                    Id = account.Id,
                    FullName = account.FullName,
                    Email = account.Email,
                    IsActive = account.IsActive,
                    Status = account.Status,
                    CreatedAt = account.CreatedAt,
                    EmailConfirmed = identityUser?.EmailConfirmed ?? false,
                    IsCurrentUser = account.Id == currentUserId
                };
            }).ToList();

            return Ok(response);
        }

        [HttpPost("superadmins")]
        public async Task<IActionResult> CreateSuperAdmin([FromBody] CreateSuperAdminDTO dto)
        {
            var email = dto.Email.Trim();
            var fullName = dto.FullName.Trim();

            if (!PasswordPolicy.TryValidate(dto.Password, out var passwordValidationMessage))
            {
                return BadRequest(passwordValidationMessage);
            }

            if (!string.Equals(dto.Password, dto.ConfirmPassword, StringComparison.Ordinal))
            {
                return BadRequest("Passwords do not match.");
            }

            if (await EmailExistsAsync(email))
            {
                return BadRequest("Email is already in use.");
            }

            var hostCompanyId = await GetHostCompanyId();
            if (hostCompanyId == 0)
            {
                return BadRequest("Host Operations company is not configured.");
            }

            var superAdminRole = await GetSuperAdminRoleAsync();
            var backupSuperAdmin = new User
            {
                CompanyId = hostCompanyId,
                Email = email,
                FullName = fullName,
                RoleId = superAdminRole.Id,
                Role = superAdminRole,
                PasswordHash = string.Empty,
                PasswordSalt = null,
                IsActive = true,
                Status = "Active"
            };

            using (var transaction = CreateTransactionScope())
            {
                _context.Users.Add(backupSuperAdmin);
                await _context.SaveChangesAsync();

                await _identityAccountService.EnsureProvisionedAsync(
                    CreateIdentitySnapshot(
                        backupSuperAdmin,
                        requireEmailConfirmation: true,
                        emailConfirmed: false),
                    dto.Password);

                transaction.Complete();
            }

            var confirmationSent = await TrySendSuperAdminConfirmationEmailAsync(backupSuperAdmin);
            await LogSuperAdminAction(
                "SUPERADMIN-CREATE",
                "SuperAdminAccount",
                backupSuperAdmin.Id,
                $"{backupSuperAdmin.FullName} ({backupSuperAdmin.Email})",
                string.Empty,
                "Active",
                confirmationSent
                    ? "Backup SuperAdmin account created. Email confirmation sent; MFA setup is recommended."
                    : "Backup SuperAdmin account created. Email confirmation could not be sent; use resend confirmation from the profile or confirmation page.");
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = confirmationSent
                    ? "Backup SuperAdmin created. Ask them to confirm email and enable MFA."
                    : "Backup SuperAdmin created, but the confirmation email could not be sent. Ask them to use resend confirmation."
            });
        }

        [HttpPut("superadmins/{id}/status")]
        public async Task<IActionResult> UpdateSuperAdminStatus(int id, [FromBody] UpdateUserStatusDTO dto)
        {
            var target = await _context.Users
                .IgnoreQueryFilters()
                .Include(user => user.Role)
                .FirstOrDefaultAsync(user => user.Id == id && !user.IsDeleted);

            if (target?.Role?.Name != "SuperAdmin")
            {
                return NotFound("SuperAdmin account not found.");
            }

            var validStatuses = new[] { "Active", "Blocked" };
            if (!validStatuses.Contains(dto.Status))
            {
                return BadRequest("Invalid status. SuperAdmin accounts can be Active or Blocked.");
            }

            var oldStatus = target.Status;
            if (string.Equals(oldStatus, dto.Status, StringComparison.Ordinal))
            {
                return Ok(new { message = $"SuperAdmin '{target.Email}' is already {dto.Status}." });
            }

            var disabling = dto.Status != "Active";
            if (disabling && target.Id == GetCurrentUserId())
            {
                return BadRequest("You cannot disable your own SuperAdmin account.");
            }

            if (disabling && await IsLastActiveSuperAdminAsync(target.Id))
            {
                await LogSuperAdminAction(
                    "SUPERADMIN-LAST-ADMIN-PROTECTION",
                    "SuperAdminAccount",
                    target.Id,
                    $"{target.FullName} ({target.Email})",
                    oldStatus,
                    dto.Status,
                    "Blocked attempt to disable the last active SuperAdmin account.");
                await _context.SaveChangesAsync();

                return BadRequest("At least one active SuperAdmin account must remain.");
            }

            target.Status = dto.Status;
            target.IsActive = dto.Status == "Active";

            using (var transaction = CreateTransactionScope())
            {
                await _context.SaveChangesAsync();
                await _identityAccountService.SyncExistingAsync(CreateIdentitySnapshot(target));

                await LogSuperAdminAction(
                    target.IsActive ? "SUPERADMIN-ENABLE" : "SUPERADMIN-DISABLE",
                    "SuperAdminAccount",
                    target.Id,
                    $"{target.FullName} ({target.Email})",
                    oldStatus,
                    dto.Status,
                    $"SuperAdmin account '{target.Email}' status changed from {oldStatus} to {dto.Status}.");
                await _context.SaveChangesAsync();

                transaction.Complete();
            }

            return Ok(new { message = $"SuperAdmin '{target.Email}' is now {dto.Status}." });
        }

        [HttpPut("users/{id}/status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateUserStatusDTO dto)
        {
            var user = await _context.Users.IgnoreQueryFilters()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound("User not found.");

            if (user.Role.Name == "SuperAdmin")
                return BadRequest("Cannot modify SuperAdmin accounts.");

            var currentUserId = GetCurrentUserId();
            if (user.Id == currentUserId)
                return BadRequest("You cannot modify your own account status.");

            var validStatuses = new[] { "Active", "Restricted", "Blocked" };
            if (!validStatuses.Contains(dto.Status))
                return BadRequest("Invalid status. Must be: Active, Restricted, or Blocked.");

            var oldStatus = user.Status;
            user.Status = dto.Status;
            user.IsActive = dto.Status == "Active";

            await LogSuperAdminAction("USER_STATUS_CHANGE", "User", id, $"{user.FullName} ({user.Email})", oldStatus, dto.Status,
                $"User '{user.Email}' status changed from {oldStatus} to {dto.Status}");

            await _context.SaveChangesAsync();
            await _identityBridgeService.SyncExistingUserStatusAsync(CreateIdentitySnapshot(user));

            return Ok(new { message = $"User '{user.Email}' is now {dto.Status}." });
        }

        [HttpPut("users/{id}/toggle")]
        public async Task<IActionResult> ToggleUserStatus(int id)
        {
            var user = await _context.Users.IgnoreQueryFilters()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound("User not found.");

            if (user.Role.Name == "SuperAdmin")
                return BadRequest("Cannot modify SuperAdmin accounts.");

            var currentUserId = GetCurrentUserId();
            if (user.Id == currentUserId)
                return BadRequest("You cannot suspend your own account.");

            var oldStatus = user.Status;
            user.IsActive = !user.IsActive;
            user.Status = user.IsActive ? "Active" : "Blocked";

            await LogSuperAdminAction("USER_STATUS_CHANGE", "User", id, $"{user.FullName} ({user.Email})", oldStatus, user.Status,
                $"User '{user.Email}' status toggled from {oldStatus} to {user.Status}");

            await _context.SaveChangesAsync();
            await _identityBridgeService.SyncExistingUserStatusAsync(CreateIdentitySnapshot(user));

            return Ok(new { message = user.IsActive ? "User activated." : "User blocked." });
        }

        [HttpGet("audit-logs")]
        public async Task<IActionResult> GetSuperAdminAuditLogs()
        {
            var logs = await _context.SuperAdminAuditLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(500)
                .Select(l => new SuperAdminAuditLogDTO
                {
                    Id = l.Id,
                    AdminEmail = l.AdminEmail,
                    Action = l.Action,
                    TargetType = l.TargetType,
                    TargetName = l.TargetName,
                    TargetId = l.TargetId,
                    OldValue = l.OldValue,
                    NewValue = l.NewValue,
                    Details = l.Details,
                    Timestamp = l.Timestamp
                })
                .ToListAsync();

            return Ok(logs);
        }

        private async Task<Role> GetSuperAdminRoleAsync()
        {
            return await _context.Roles
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(role => role.Name == "SuperAdmin")
                ?? throw new InvalidOperationException("System role 'SuperAdmin' is missing.");
        }

        private async Task<bool> EmailExistsAsync(string email)
        {
            var normalizedEmail = _userManager.NormalizeEmail(email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return true;
            }

            var legacyExists = await _context.Users
                .IgnoreQueryFilters()
                .AnyAsync(user => user.Email.ToUpper() == normalizedEmail && !user.IsDeleted);

            if (legacyExists)
            {
                return true;
            }

            return await _identityContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail);
        }

        private async Task<bool> IsLastActiveSuperAdminAsync(int targetUserId)
        {
            var superAdminRole = await GetSuperAdminRoleAsync();
            var activeSuperAdmins = await _context.Users
                .IgnoreQueryFilters()
                .CountAsync(user =>
                    user.RoleId == superAdminRole.Id &&
                    !user.IsDeleted &&
                    user.IsActive &&
                    user.Status == "Active" &&
                    user.Id != targetUserId);

            return activeSuperAdmins == 0;
        }

        private async Task<bool> TrySendSuperAdminConfirmationEmailAsync(User superAdmin)
        {
            try
            {
                var identityUser = await _identityAccountService.FindByLegacyUserIdAsync(superAdmin.Id)
                    ?? throw new InvalidOperationException($"Identity user was not found for SuperAdmin {superAdmin.Id}.");

                var token = await _userManager.GenerateEmailConfirmationTokenAsync(identityUser);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var confirmationLink = BuildEmailConfirmationLink(identityUser.Email!, encodedToken);

                await _accountEmailService.SendEmailConfirmationAsync(
                    identityUser.Email!,
                    identityUser.FullName,
                    confirmationLink);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send backup SuperAdmin confirmation email for user {UserId}.", superAdmin.Id);
                return false;
            }
        }

        private string BuildEmailConfirmationLink(string email, string encodedToken)
        {
            var clientBaseUrl = ResolveClientBaseUrl();
            return $"{clientBaseUrl}/confirm-email?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(encodedToken)}";
        }

        private string ResolveClientBaseUrl()
        {
            var request = HttpContext?.Request;
            if (request != null &&
                TryNormalizeAbsoluteBaseUrl(request.Headers.Origin.ToString(), out var originBaseUrl))
            {
                return originBaseUrl!;
            }

            if (request != null &&
                TryNormalizeAbsoluteBaseUrl(request.Headers.Referer.ToString(), out var refererBaseUrl))
            {
                return refererBaseUrl!;
            }

            var configuredBaseUrl = NormalizeBaseUrl(_configuration["AppUrls:ClientBaseUrl"]);
            if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
            {
                return configuredBaseUrl;
            }

            throw new InvalidOperationException(StartupConfigurationValidator.BuildMissingValueMessage("AppUrls:ClientBaseUrl"));
        }

        private static bool TryNormalizeAbsoluteBaseUrl(string? value, out string? normalizedBaseUrl)
        {
            normalizedBaseUrl = null;
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri) ||
                (absoluteUri.Scheme != Uri.UriSchemeHttp && absoluteUri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            normalizedBaseUrl = NormalizeBaseUrl(absoluteUri.GetLeftPart(UriPartial.Authority));
            return true;
        }

        private static string NormalizeBaseUrl(string? baseUrl) =>
            string.IsNullOrWhiteSpace(baseUrl)
                ? string.Empty
                : baseUrl.Trim().TrimEnd('/');

        private Task LogSuperAdminAction(string action, string targetType, int targetId, string targetName,
            string oldValue, string newValue, string details)
        {
            var log = new SuperAdminAuditLog
            {
                AdminUserId = GetCurrentUserId(),
                AdminEmail = GetCurrentUserEmail(),
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                TargetName = targetName,
                OldValue = oldValue,
                NewValue = newValue,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            _context.SuperAdminAuditLogs.Add(log);
            return Task.CompletedTask;
        }

        private static TransactionScope CreateTransactionScope()
        {
            return new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        }

        private static LegacyIdentityUserSnapshot CreateIdentitySnapshot(
            User user,
            bool? requireEmailConfirmation = null,
            bool? emailConfirmed = null) =>
            new(
                user.Id,
                user.CompanyId,
                user.Email,
                user.FullName ?? user.Email,
                user.Status,
                user.IsActive,
                user.IsDeleted,
                user.Role.Name,
                requireEmailConfirmation,
                emailConfirmed);
    }
}
