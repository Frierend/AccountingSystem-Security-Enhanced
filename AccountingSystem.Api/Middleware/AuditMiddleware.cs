using AccountingSystem.API.Data;
using AccountingSystem.API.Models;
using AccountingSystem.API.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace AccountingSystem.API.Middleware
{
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuditMiddleware> _logger;

        public AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware>? logger = null)
        {
            _next = next;
            _logger = logger ?? NullLogger<AuditMiddleware>.Instance;
        }

        public async Task Invoke(HttpContext context, AccountingDbContext dbContext)
        {
            var method = context.Request.Method;
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            var shouldLog = method == "POST" || method == "PUT" || method == "DELETE";

            if (!shouldLog || path.StartsWith("/api/auth"))
            {
                await _next(context);
                return;
            }

            var bodyContent = string.Empty;
            try
            {
                context.Request.EnableBuffering();
                using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 1024, true))
                {
                    bodyContent = await reader.ReadToEndAsync();
                }

                context.Request.Body.Position = 0;
            }
            catch
            {
                bodyContent = "[Error reading body]";
            }

            int? userId = null;
            if (context.Items["UserId"] is string userIdStr && int.TryParse(userIdStr, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            var companyId = 0;
            if (context.Items["CompanyId"] is string companyIdStr && int.TryParse(companyIdStr, out var parsedCompanyId))
            {
                companyId = parsedCompanyId;
            }

            await _next(context);

            if (context.Response.StatusCode < 200 || context.Response.StatusCode >= 300)
            {
                return;
            }

            try
            {
                var action = method;

                if (path.Contains("/api/users"))
                {
                    if (path.EndsWith("/restore"))
                    {
                        action = "USER-RESTORE";
                    }
                    else if (action == "POST")
                    {
                        action = "USER-CREATE";
                    }
                    else if (action == "DELETE")
                    {
                        action = "USER-ARCHIVE";
                    }
                }
                else if (path.Contains("/receivables/customers"))
                {
                    if (path.EndsWith("/restore")) action = "CUSTOMER-RESTORE";
                    else if (action == "POST") action = "CUSTOMER-CREATE";
                    else if (action == "PUT") action = "CUSTOMER-UPDATE";
                    else if (action == "DELETE") action = "CUSTOMER-ARCHIVE";
                }
                else if (path.Contains("/payables/vendors"))
                {
                    if (path.EndsWith("/restore")) action = "VENDOR-RESTORE";
                    else if (action == "POST") action = "VENDOR-CREATE";
                    else if (action == "PUT") action = "VENDOR-UPDATE";
                    else if (action == "DELETE") action = "VENDOR-ARCHIVE";
                }
                else if (path.Contains("/ledger/accounts"))
                {
                    if (path.EndsWith("/restore")) action = "ACCOUNT-RESTORE";
                    else if (action == "POST") action = "ACCOUNT-CREATE";
                    else if (action == "PUT") action = "ACCOUNT-UPDATE";
                    else if (action == "DELETE") action = "ACCOUNT-ARCHIVE";
                }
                else if (path.Contains("/bill") && action == "POST")
                {
                    action = path.Contains("/pay") ? "BILL-PAY" : "BILL-CREATE";
                }
                else if (path.Contains("/invoice") && action == "POST")
                {
                    action = path.Contains("/receive") ? "INVOICE-PAYMENT" : "INVOICE-CREATE";
                }
                else if (path.Contains("/journal") && action == "POST")
                {
                    action = "JOURNAL-ENTRY";
                }
                else if (path.Contains("/companies/current") && action == "PUT")
                {
                    action = "COMPANY-UPDATE";
                }
                else if (path.Contains("/superadmin/companies") && path.Contains("/status"))
                {
                    action = "SUPERADMIN-COMPANY-STATUS";
                }
                else if (path.Contains("/superadmin/users") && path.Contains("/status"))
                {
                    action = "SUPERADMIN-USER-STATUS";
                }

                var entityId = ResolveEntityId(context, path);
                var changesMetadata = new
                {
                    category = AuditLogSanitizer.DeriveCategory(action),
                    method,
                    path = context.Request.Path.Value ?? "N/A",
                    statusCode = context.Response.StatusCode,
                    result = "Success",
                    resource = ResolveResource(path),
                    entityId,
                    request = AuditLogSanitizer.CreateRequestPayloadSummary(bodyContent, context.Request.ContentType)
                };

                var auditLog = new AuditLog
                {
                    UserId = userId,
                    CompanyId = companyId,
                    Action = action,
                    EntityName = context.Request.Path,
                    EntityId = entityId,
                    Timestamp = DateTime.UtcNow,
                    Changes = AuditLogSanitizer.SerializeAndTrim(changesMetadata)
                };

                dbContext.AuditLogs.Add(auditLog);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit logging failed for {Method} {Path}.", method, context.Request.Path.Value);
            }
        }

        private static string ResolveEntityId(HttpContext context, string path)
        {
            if (context.Request.RouteValues.TryGetValue("id", out var routeId) &&
                routeId != null &&
                !string.IsNullOrWhiteSpace(routeId.ToString()))
            {
                return routeId.ToString()!;
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var candidate = segments.LastOrDefault();
            return int.TryParse(candidate, out _) ? candidate! : "N/A";
        }

        private static string ResolveResource(string path)
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return "unknown";
            }

            if (segments.Length > 1 && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase))
            {
                return segments[1];
            }

            return segments[0];
        }
    }
}
