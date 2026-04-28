using AccountingSystem.API.Data;
using AccountingSystem.API.Models;
using AccountingSystem.API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AccountingSystem.API.Services
{
    public class AuthSecurityAuditService : IAuthSecurityAuditService
    {
        private readonly AccountingDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuthSecurityAuditService> _logger;

        public AuthSecurityAuditService(
            AccountingDbContext context,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuthSecurityAuditService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public async Task WriteAsync(
            string action,
            int? userId = null,
            int? companyId = null,
            string? email = null,
            string? reason = null,
            int? failedAttempts = null,
            DateTime? lockoutEndUtc = null,
            string? policy = null)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var path = httpContext?.Request.Path.Value ?? "N/A";
                var remoteIpAddress = httpContext?.Connection.RemoteIpAddress?.ToString();
                var resolvedUserId = userId ?? TryParseClaim(httpContext, "UserId");
                var resolvedCompanyId = companyId ?? TryParseClaim(httpContext, "CompanyId") ?? 0;
                var resolvedEmail = email ?? httpContext?.User?.Identity?.Name;

                var metadata = new
                {
                    category = "Security",
                    path,
                    remoteIpAddress,
                    email = resolvedEmail,
                    reason,
                    failedAttempts,
                    lockoutEndUtc,
                    policy
                };

                var auditLog = new AuditLog
                {
                    UserId = resolvedUserId,
                    CompanyId = resolvedCompanyId,
                    Action = action,
                    EntityName = path,
                    EntityId = "N/A",
                    Timestamp = DateTime.UtcNow,
                    Changes = AuditLogSanitizer.SerializeAndTrim(metadata)
                };

                _context.AuditLogs.Add(auditLog);
                await TryWriteSuperAdminSecurityEventAsync(
                    action,
                    resolvedUserId,
                    resolvedEmail,
                    reason,
                    failedAttempts,
                    lockoutEndUtc,
                    policy,
                    path,
                    remoteIpAddress);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write auth security audit event {Action}.", action);
            }
        }

        private static int? TryParseClaim(HttpContext? httpContext, string claimType)
        {
            var value = httpContext?.User?.FindFirst(claimType)?.Value;
            return int.TryParse(value, out var parsedValue) ? parsedValue : null;
        }

        private async Task TryWriteSuperAdminSecurityEventAsync(
            string action,
            int? userId,
            string? email,
            string? reason,
            int? failedAttempts,
            DateTime? lockoutEndUtc,
            string? policy,
            string path,
            string? remoteIpAddress)
        {
            if (!TryMapSuperAdminSecurityAction(action, out var superAdminAction))
            {
                return;
            }

            var targetUser = await ResolveSuperAdminTargetAsync(userId, email);
            if (targetUser == null)
            {
                return;
            }

            var details = BuildSuperAdminSecurityDetails(
                action,
                reason,
                failedAttempts,
                lockoutEndUtc,
                policy,
                path,
                remoteIpAddress);

            _context.SuperAdminAuditLogs.Add(new SuperAdminAuditLog
            {
                AdminUserId = targetUser.Id,
                AdminEmail = targetUser.Email,
                Action = superAdminAction,
                TargetType = "SuperAdminAccount",
                TargetId = targetUser.Id,
                TargetName = targetUser.Email,
                OldValue = string.Empty,
                NewValue = string.Empty,
                Details = details,
                Timestamp = DateTime.UtcNow
            });
        }

        private async Task<User?> ResolveSuperAdminTargetAsync(int? userId, string? email)
        {
            IQueryable<User> query = _context.Users
                .IgnoreQueryFilters()
                .Include(user => user.Role);

            var targetUser = userId.HasValue
                ? await query.FirstOrDefaultAsync(user => user.Id == userId.Value)
                : null;

            if (targetUser == null && !string.IsNullOrWhiteSpace(email))
            {
                var normalizedEmail = email.Trim().ToUpperInvariant();
                targetUser = await query.FirstOrDefaultAsync(user => user.Email.ToUpper() == normalizedEmail);
            }

            return targetUser?.Role?.Name == "SuperAdmin"
                ? targetUser
                : null;
        }

        private static bool TryMapSuperAdminSecurityAction(string action, out string superAdminAction)
        {
            superAdminAction = action switch
            {
                "AUTH-LOGIN-FAILURE" => "SUPERADMIN-AUTH-LOGIN-FAILURE",
                "AUTH-LOCKOUT-APPLIED" => "SUPERADMIN-AUTH-LOCKOUT",
                "AUTH-LOCKOUT-BLOCKED" => "SUPERADMIN-AUTH-LOCKOUT",
                "AUTH-LOGIN-CAPTCHA-REQUIRED" => "SUPERADMIN-AUTH-CAPTCHA-REQUIRED",
                "AUTH-MFA-LOGIN-CHALLENGE" => "SUPERADMIN-AUTH-MFA-CHALLENGE",
                "AUTH-LOGIN-SUCCESS" => "SUPERADMIN-AUTH-LOGIN-SUCCESS",
                "AUTH-MFA-LOGIN-SUCCESS" => "SUPERADMIN-AUTH-LOGIN-SUCCESS",
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(superAdminAction);
        }

        private static string BuildSuperAdminSecurityDetails(
            string sourceAction,
            string? reason,
            int? failedAttempts,
            DateTime? lockoutEndUtc,
            string? policy,
            string path,
            string? remoteIpAddress)
        {
            var details = new List<string>
            {
                $"Source={sourceAction}",
                $"Reason={reason ?? "N/A"}",
                $"FailedAttempts={(failedAttempts.HasValue ? failedAttempts.Value.ToString() : "N/A")}",
                $"LockoutEndUtc={(lockoutEndUtc.HasValue ? lockoutEndUtc.Value.ToString("O") : "N/A")}",
                $"Policy={policy ?? "N/A"}",
                $"Path={path}",
                $"RemoteIp={remoteIpAddress ?? "N/A"}"
            };

            var value = string.Join("; ", details);
            return value.Length <= 500 ? value : value[..500];
        }
    }
}
