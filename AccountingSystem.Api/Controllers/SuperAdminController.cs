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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
        private readonly IEmailOtpChallengeStore _emailOtpChallengeStore;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;

        private const string EmailOtpLoginProvider = "AccSysEmailOtpMfa";
        private const string EmailOtpEnabledTokenName = "Enabled";
        private const string PasswordOnlyStepUpMethod = "PasswordOnly";
        private const int DefaultEmailOtpExpirationMinutes = 5;
        private const int DefaultEmailOtpMaxVerificationAttempts = 3;
        private const int DefaultEmailOtpResendCooldownSeconds = 60;
        private const int MaxStepUpReasonLength = 240;
        private static readonly Regex ConsecutiveWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex SuspiciousReasonValueRegex = new(
            @"(?i)(password|otp|recovery\s*code|token|jwt|secret|api\s*key|smtp|paymongo|captcha)",
            RegexOptions.Compiled);
        private static readonly Regex LongTokenLikeValueRegex = new(
            @"\b[A-Za-z0-9_\-]{24,}\b",
            RegexOptions.Compiled);

        public SuperAdminController(
            AccountingDbContext context,
            ILogger<SuperAdminController> logger,
            ILegacyIdentityBridgeService identityBridgeService,
            IdentityAuthDbContext identityContext,
            IIdentityAccountService identityAccountService,
            IAccountEmailService accountEmailService,
            IEmailOtpChallengeStore emailOtpChallengeStore,
            UserManager<ApplicationUser> userManager,
            IConfiguration configuration)
        {
            _context = context;
            _identityContext = identityContext;
            _logger = logger;
            _identityBridgeService = identityBridgeService;
            _identityAccountService = identityAccountService;
            _accountEmailService = accountEmailService;
            _emailOtpChallengeStore = emailOtpChallengeStore;
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
        public async Task<IActionResult> CreateSuperAdmin([FromBody] CreateSuperAdminRequestDTO request)
        {
            if (!User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }

            if (request?.SuperAdmin == null || request.StepUp == null)
            {
                return BadRequest(new { message = "Step-up verification payload is required." });
            }

            var dto = request.SuperAdmin;
            var email = (dto.Email ?? string.Empty).Trim();
            var fullName = (dto.FullName ?? string.Empty).Trim();

            var stepUpResult = await VerifySensitiveSuperAdminActionAsync(
                request.StepUp,
                targetType: "SuperAdminAccount",
                targetId: 0,
                targetName: email,
                actionName: "SUPERADMIN-CREATE");
            if (!stepUpResult.Succeeded)
            {
                return StatusCode(stepUpResult.StatusCode, new { message = stepUpResult.Message });
            }

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
                    ? BuildGovernanceDetails(
                        "Backup SuperAdmin account created. Email confirmation sent; MFA setup is recommended.",
                        stepUpResult.SanitizedReason,
                        stepUpResult.StepUpMethod,
                        stepUpResult.MfaRequired)
                    : BuildGovernanceDetails(
                        "Backup SuperAdmin account created. Email confirmation could not be sent; use resend confirmation from the profile or confirmation page.",
                        stepUpResult.SanitizedReason,
                        stepUpResult.StepUpMethod,
                        stepUpResult.MfaRequired));
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = confirmationSent
                    ? "Backup SuperAdmin created. Ask them to confirm email and enable MFA."
                    : "Backup SuperAdmin created, but the confirmation email could not be sent. Ask them to use resend confirmation."
            });
        }

        [HttpPost("stepup/email/send")]
        [EnableRateLimiting(AuthRateLimitPolicyNames.MfaManage)]
        public async Task<IActionResult> SendStepUpEmailOtp()
        {
            if (!User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }

            var actor = await ResolveCurrentSuperAdminAsync();
            if (actor == null)
            {
                return Forbid();
            }

            var identityUser = await _identityAccountService.FindByLegacyUserIdAsync(actor.Id);
            if (identityUser == null)
            {
                await LogSuperAdminAction(
                    "SUPERADMIN-STEPUP-FAILED",
                    "SuperAdminAccount",
                    actor.Id,
                    $"{actor.FullName} ({actor.Email})",
                    string.Empty,
                    "Failed",
                    "Step-up verification failed. Result=IdentityUnavailable; Action=SEND-STEPUP-EMAIL-OTP; Reason=N/A.");
                await _context.SaveChangesAsync();
                return BadRequest(new { message = "Unable to send a verification code right now." });
            }

            var emailOtpEnabled = await IsEmailOtpEnabledAsync(identityUser);
            var emailConfirmed = await _userManager.IsEmailConfirmedAsync(identityUser);
            if (!emailOtpEnabled || !emailConfirmed || string.IsNullOrWhiteSpace(identityUser.Email))
            {
                await LogSuperAdminAction(
                    "SUPERADMIN-STEPUP-MFA-REQUIRED",
                    "SuperAdminAccount",
                    actor.Id,
                    $"{actor.FullName} ({actor.Email})",
                    string.Empty,
                    "EmailOtpUnavailable",
                    "Step-up Email OTP is unavailable for the acting SuperAdmin account.");
                await _context.SaveChangesAsync();
                return BadRequest(new { message = "Email OTP MFA is not available for this account." });
            }

            var code = GenerateEmailOtpCode();
            var issueResult = _emailOtpChallengeStore.Issue(
                BuildSuperAdminEmailOtpChallengeKey(identityUser.Id),
                identityUser.Id,
                actor.Id,
                code,
                GetEmailOtpExpiration(),
                GetEmailOtpResendCooldown());

            if (!issueResult.Succeeded)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = "Please wait before requesting another email verification code."
                });
            }

            try
            {
                await _accountEmailService.SendEmailOtpAsync(
                    identityUser.Email,
                    actor.FullName,
                    code,
                    GetEmailOtpExpirationMinutes());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send step-up email OTP for SuperAdmin user id {UserId}.", actor.Id);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Unable to send a verification code right now."
                });
            }

            await LogSuperAdminAction(
                "SUPERADMIN-STEPUP-MFA-REQUIRED",
                "SuperAdminAccount",
                actor.Id,
                $"{actor.FullName} ({actor.Email})",
                string.Empty,
                "EmailOtpIssued",
                "Step-up Email OTP challenge issued for a sensitive SuperAdmin governance action.");
            await _context.SaveChangesAsync();

            return Ok(new { message = "Verification code sent to your email." });
        }

        [HttpPut("superadmins/{id}/status")]
        public async Task<IActionResult> UpdateSuperAdminStatus(int id, [FromBody] UpdateSuperAdminStatusRequestDTO request)
        {
            if (!User.IsInRole("SuperAdmin"))
            {
                return Forbid();
            }

            if (request?.StepUp == null)
            {
                return BadRequest(new { message = "Step-up verification payload is required." });
            }

            var dto = new UpdateUserStatusDTO
            {
                Status = request.Status
            };

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

            var stepUpResult = await VerifySensitiveSuperAdminActionAsync(
                request.StepUp,
                targetType: "SuperAdminAccount",
                targetId: target.Id,
                targetName: target.Email,
                actionName: dto.Status == "Active" ? "SUPERADMIN-ENABLE" : "SUPERADMIN-DISABLE");
            if (!stepUpResult.Succeeded)
            {
                return StatusCode(stepUpResult.StatusCode, new { message = stepUpResult.Message });
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
                    BuildGovernanceDetails(
                        $"SuperAdmin account '{target.Email}' status changed from {oldStatus} to {dto.Status}.",
                        stepUpResult.SanitizedReason,
                        stepUpResult.StepUpMethod,
                        stepUpResult.MfaRequired));
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

        private async Task<User?> ResolveCurrentSuperAdminAsync()
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId <= 0)
            {
                return null;
            }

            var actor = await _context.Users
                .IgnoreQueryFilters()
                .Include(user => user.Role)
                .FirstOrDefaultAsync(user => user.Id == currentUserId && !user.IsDeleted);

            if (actor?.Role?.Name != "SuperAdmin")
            {
                return null;
            }

            return actor;
        }

        private async Task<SuperAdminStepUpResult> VerifySensitiveSuperAdminActionAsync(
            SuperAdminStepUpVerificationDTO? stepUp,
            string targetType,
            int targetId,
            string targetName,
            string actionName)
        {
            if (stepUp == null)
            {
                return new SuperAdminStepUpResult(
                    false,
                    StatusCodes.Status400BadRequest,
                    "Step-up verification payload is required.");
            }

            var actor = await ResolveCurrentSuperAdminAsync();
            if (actor == null)
            {
                return new SuperAdminStepUpResult(false, StatusCodes.Status403Forbidden, "Access denied.");
            }

            var sanitizedReason = SanitizeReasonForAudit(stepUp.Reason);
            if (string.IsNullOrWhiteSpace(sanitizedReason))
            {
                await LogSuperAdminAction(
                    "SUPERADMIN-STEPUP-FAILED",
                    targetType,
                    targetId,
                    targetName,
                    string.Empty,
                    "Failed",
                    $"Step-up verification failed. Result=ReasonRequired; Action={actionName}; Reason=N/A.");
                await _context.SaveChangesAsync();
                return new SuperAdminStepUpResult(false, StatusCodes.Status400BadRequest, "Reason is required for governance audit logging.");
            }

            if (stepUp.Reason?.Length > MaxStepUpReasonLength)
            {
                await LogSuperAdminAction(
                    "SUPERADMIN-STEPUP-FAILED",
                    targetType,
                    targetId,
                    targetName,
                    string.Empty,
                    "Failed",
                    $"Step-up verification failed. Result=ReasonTooLong; Action={actionName}; Reason={sanitizedReason}.");
                await _context.SaveChangesAsync();
                return new SuperAdminStepUpResult(false, StatusCodes.Status400BadRequest, $"Reason must be {MaxStepUpReasonLength} characters or less.");
            }

            if (string.IsNullOrWhiteSpace(stepUp.CurrentPassword))
            {
                await LogSuperAdminAction(
                    "SUPERADMIN-STEPUP-FAILED",
                    targetType,
                    targetId,
                    targetName,
                    string.Empty,
                    "Failed",
                    $"Step-up verification failed. Result=CurrentPasswordRequired; Action={actionName}; Reason={sanitizedReason}.");
                await _context.SaveChangesAsync();
                return new SuperAdminStepUpResult(false, StatusCodes.Status400BadRequest, "Current password is required.");
            }

            var identityUser = await _identityAccountService.FindByLegacyUserIdAsync(actor.Id);
            if (identityUser == null)
            {
                await LogSuperAdminAction(
                    "SUPERADMIN-STEPUP-FAILED",
                    targetType,
                    targetId,
                    targetName,
                    string.Empty,
                    "Failed",
                    $"Step-up verification failed. Result=IdentityUnavailable; Action={actionName}; Reason={sanitizedReason}.");
                await _context.SaveChangesAsync();
                return new SuperAdminStepUpResult(false, StatusCodes.Status400BadRequest, "Identity verification could not be completed.");
            }

            if (!await _userManager.CheckPasswordAsync(identityUser, stepUp.CurrentPassword))
            {
                await LogSuperAdminAction(
                    "SUPERADMIN-STEPUP-FAILED",
                    targetType,
                    targetId,
                    targetName,
                    string.Empty,
                    "Failed",
                    $"Step-up verification failed. Result=InvalidCurrentPassword; Action={actionName}; Reason={sanitizedReason}.");
                await _context.SaveChangesAsync();
                return new SuperAdminStepUpResult(false, StatusCodes.Status401Unauthorized, "Identity verification failed.");
            }

            var authenticatorEnabled = await IsAuthenticatorAppEnabledAsync(identityUser);
            var emailOtpEnabled = await IsEmailOtpEnabledAsync(identityUser);
            var mfaRequired = authenticatorEnabled || emailOtpEnabled;
            var stepUpMethod = PasswordOnlyStepUpMethod;

            if (mfaRequired)
            {
                var mfaVerification = await VerifyStepUpMfaAsync(stepUp, identityUser, actor.Id, authenticatorEnabled, emailOtpEnabled);
                if (!mfaVerification.Succeeded)
                {
                    await LogSuperAdminAction(
                        "SUPERADMIN-STEPUP-FAILED",
                        targetType,
                        targetId,
                        targetName,
                        string.Empty,
                        "Failed",
                        $"Step-up verification failed. Result={mfaVerification.FailureReason}; Action={actionName}; Reason={sanitizedReason}; MfaRequired=true; MfaMethod={mfaVerification.StepUpMethod}.");
                    await _context.SaveChangesAsync();
                    return new SuperAdminStepUpResult(false, StatusCodes.Status401Unauthorized, "Identity verification failed.");
                }

                stepUpMethod = mfaVerification.StepUpMethod;
            }

            await LogSuperAdminAction(
                "SUPERADMIN-STEPUP-SUCCESS",
                targetType,
                targetId,
                targetName,
                string.Empty,
                "Verified",
                $"Step-up verification passed. Action={actionName}; Reason={sanitizedReason}; MfaRequired={mfaRequired}; MfaMethod={stepUpMethod}.");
            await _context.SaveChangesAsync();

            return new SuperAdminStepUpResult(
                true,
                StatusCodes.Status200OK,
                string.Empty,
                sanitizedReason,
                mfaRequired,
                stepUpMethod);
        }

        private async Task<(bool Succeeded, string FailureReason, string StepUpMethod)> VerifyStepUpMfaAsync(
            SuperAdminStepUpVerificationDTO stepUp,
            ApplicationUser identityUser,
            int legacyUserId,
            bool authenticatorEnabled,
            bool emailOtpEnabled)
        {
            var requestedMethod = NormalizeMfaMethod(stepUp.MfaMethod);
            var hasMfaCode = !string.IsNullOrWhiteSpace(stepUp.MfaCode);
            var hasRecoveryCode = !string.IsNullOrWhiteSpace(stepUp.RecoveryCode);

            if (hasRecoveryCode && string.IsNullOrWhiteSpace(requestedMethod))
            {
                requestedMethod = MfaLoginMethods.RecoveryCode;
            }

            if (string.IsNullOrWhiteSpace(requestedMethod))
            {
                if (authenticatorEnabled && emailOtpEnabled)
                {
                    return (false, "MfaMethodRequired", "Unknown");
                }

                if (authenticatorEnabled)
                {
                    requestedMethod = MfaLoginMethods.AuthenticatorApp;
                }
                else if (emailOtpEnabled)
                {
                    requestedMethod = MfaLoginMethods.EmailOtp;
                }
            }

            if (string.Equals(requestedMethod, MfaLoginMethods.RecoveryCode, StringComparison.Ordinal))
            {
                if (!authenticatorEnabled)
                {
                    return (false, "RecoveryCodeUnavailable", requestedMethod);
                }

                if (!hasRecoveryCode)
                {
                    return (false, "RecoveryCodeRequired", requestedMethod);
                }

                var recoveryResult = await RedeemRecoveryCodeAsync(identityUser, stepUp.RecoveryCode);
                return recoveryResult.Succeeded
                    ? (true, string.Empty, requestedMethod)
                    : (false, "InvalidRecoveryCode", requestedMethod);
            }

            if (string.Equals(requestedMethod, MfaLoginMethods.EmailOtp, StringComparison.Ordinal))
            {
                if (!emailOtpEnabled || !await _userManager.IsEmailConfirmedAsync(identityUser))
                {
                    return (false, "EmailOtpUnavailable", requestedMethod);
                }

                if (!hasMfaCode)
                {
                    return (false, "EmailOtpCodeRequired", requestedMethod);
                }

                var verificationResult = _emailOtpChallengeStore.Verify(
                    BuildSuperAdminEmailOtpChallengeKey(identityUser.Id),
                    identityUser.Id,
                    legacyUserId,
                    stepUp.MfaCode,
                    GetEmailOtpMaxVerificationAttempts());

                return verificationResult.Succeeded
                    ? (true, string.Empty, requestedMethod)
                    : (false, $"EmailOtp{verificationResult.Status}", requestedMethod);
            }

            if (string.Equals(requestedMethod, MfaLoginMethods.AuthenticatorApp, StringComparison.Ordinal))
            {
                if (!authenticatorEnabled)
                {
                    return (false, "AuthenticatorUnavailable", requestedMethod);
                }

                if (!hasMfaCode)
                {
                    return (false, "AuthenticatorCodeRequired", requestedMethod);
                }

                var sanitizedCode = NormalizeAuthenticatorCode(stepUp.MfaCode);
                if (sanitizedCode.Length != 6 ||
                    !sanitizedCode.All(char.IsDigit) ||
                    !await _userManager.VerifyTwoFactorTokenAsync(
                        identityUser,
                        _userManager.Options.Tokens.AuthenticatorTokenProvider,
                        sanitizedCode))
                {
                    return (false, "InvalidAuthenticatorCode", requestedMethod);
                }

                return (true, string.Empty, requestedMethod);
            }

            return (false, "UnsupportedMfaMethod", string.IsNullOrWhiteSpace(requestedMethod) ? "Unknown" : requestedMethod);
        }

        private async Task<bool> IsAuthenticatorAppEnabledAsync(ApplicationUser identityUser)
        {
            if (!await _userManager.GetTwoFactorEnabledAsync(identityUser))
            {
                return false;
            }

            var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(identityUser);
            return !string.IsNullOrWhiteSpace(authenticatorKey);
        }

        private async Task<bool> IsEmailOtpEnabledAsync(ApplicationUser identityUser)
        {
            var value = await _userManager.GetAuthenticationTokenAsync(
                identityUser,
                EmailOtpLoginProvider,
                EmailOtpEnabledTokenName);

            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeMfaMethod(string? method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return string.Empty;
            }

            if (string.Equals(method, MfaLoginMethods.EmailOtp, StringComparison.OrdinalIgnoreCase))
            {
                return MfaLoginMethods.EmailOtp;
            }

            if (string.Equals(method, MfaLoginMethods.RecoveryCode, StringComparison.OrdinalIgnoreCase))
            {
                return MfaLoginMethods.RecoveryCode;
            }

            if (string.Equals(method, MfaLoginMethods.AuthenticatorApp, StringComparison.OrdinalIgnoreCase))
            {
                return MfaLoginMethods.AuthenticatorApp;
            }

            return method.Trim();
        }

        private static string NormalizeAuthenticatorCode(string code)
        {
            return new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        private static string NormalizeRecoveryCode(string code)
        {
            return new string((code ?? string.Empty)
                .Where(character => !char.IsWhiteSpace(character) && character != '-')
                .ToArray());
        }

        private async Task<IdentityResult> RedeemRecoveryCodeAsync(ApplicationUser identityUser, string recoveryCode)
        {
            var trimmedRecoveryCode = recoveryCode.Trim();
            if (string.IsNullOrWhiteSpace(trimmedRecoveryCode))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "InvalidRecoveryCode",
                    Description = "The recovery code is invalid."
                });
            }

            var normalizedRecoveryCode = NormalizeRecoveryCode(trimmedRecoveryCode);
            var candidates = new[] { trimmedRecoveryCode, normalizedRecoveryCode }
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.Ordinal);

            IdentityResult? lastResult = null;
            foreach (var candidate in candidates)
            {
                lastResult = await _userManager.RedeemTwoFactorRecoveryCodeAsync(identityUser, candidate);
                if (lastResult.Succeeded)
                {
                    return lastResult;
                }
            }

            return lastResult ?? IdentityResult.Failed(new IdentityError
            {
                Code = "InvalidRecoveryCode",
                Description = "The recovery code is invalid."
            });
        }

        private static string BuildGovernanceDetails(string summary, string reason, string stepUpMethod, bool mfaRequired)
        {
            var details = $"{summary} Reason={reason}; StepUpMethod={stepUpMethod}; MfaRequired={mfaRequired}.";
            return details.Length <= 500 ? details : details[..500];
        }

        private static string SanitizeReasonForAudit(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return string.Empty;
            }

            var withoutControlCharacters = new string(reason
                .Where(character => !char.IsControl(character))
                .ToArray());
            var normalizedWhitespace = ConsecutiveWhitespaceRegex.Replace(withoutControlCharacters.Trim(), " ");
            var truncated = normalizedWhitespace.Length <= MaxStepUpReasonLength
                ? normalizedWhitespace
                : normalizedWhitespace[..MaxStepUpReasonLength];

            if (SuspiciousReasonValueRegex.IsMatch(truncated))
            {
                return "[REDACTED-SENSITIVE-REASON]";
            }

            return LongTokenLikeValueRegex.Replace(truncated, "[REDACTED]");
        }

        private static string BuildSuperAdminEmailOtpChallengeKey(int identityUserId)
        {
            return $"superadmin-stepup-email:{identityUserId}";
        }

        private int GetEmailOtpExpirationMinutes()
        {
            var configuredValue = _configuration.GetValue<int?>("Mfa:EmailOtpExpirationMinutes");
            return configuredValue is > 0 ? configuredValue.Value : DefaultEmailOtpExpirationMinutes;
        }

        private TimeSpan GetEmailOtpExpiration()
        {
            return TimeSpan.FromMinutes(GetEmailOtpExpirationMinutes());
        }

        private TimeSpan GetEmailOtpResendCooldown()
        {
            var configuredValue = _configuration.GetValue<int?>("Mfa:EmailOtpResendCooldownSeconds");
            var seconds = configuredValue is > 0 ? configuredValue.Value : DefaultEmailOtpResendCooldownSeconds;
            return TimeSpan.FromSeconds(seconds);
        }

        private int GetEmailOtpMaxVerificationAttempts()
        {
            var configuredValue = _configuration.GetValue<int?>("Mfa:EmailOtpMaxVerificationAttempts");
            return configuredValue is > 0 ? configuredValue.Value : DefaultEmailOtpMaxVerificationAttempts;
        }

        private static string GenerateEmailOtpCode()
        {
            return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
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

        private sealed record SuperAdminStepUpResult(
            bool Succeeded,
            int StatusCode,
            string Message,
            string SanitizedReason = "",
            bool MfaRequired = false,
            string StepUpMethod = PasswordOnlyStepUpMethod);

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
