using AccountingSystem.API.Services.Interfaces;
using AccountingSystem.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccountingSystem.API.Controllers
{
    [ApiController]
    [Route("api/ledger")]
    public class GeneralLedgerController : ControllerBase
    {
        private readonly ILedgerService _ledgerService;
        private readonly IYearEndCloseService _yearEndCloseService;
        private readonly ILogger<GeneralLedgerController> _logger;

        public GeneralLedgerController(
            ILedgerService ledgerService,
            IYearEndCloseService yearEndCloseService,
            ILogger<GeneralLedgerController>? logger = null)
        {
            _ledgerService = ledgerService;
            _yearEndCloseService = yearEndCloseService;
            _logger = logger ?? NullLogger<GeneralLedgerController>.Instance;
        }

        [HttpGet("accounts")]
        [Authorize(Roles = "Admin,Accounting,Management")]
        public async Task<IActionResult> GetChartOfAccounts([FromQuery] bool includeArchived = false)
        {
            var accounts = await _ledgerService.GetChartOfAccountsAsync(includeArchived);
            var dtos = accounts.Select(a => new AccountDTO
            {
                Id = a.Id,
                Code = a.Code,
                Name = a.Name,
                Type = a.Type,
                IsActive = a.IsActive,
                IsDeleted = a.IsDeleted
            }).ToList();

            return Ok(dtos);
        }

        [HttpPost("accounts")]
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> CreateAccount([FromBody] CreateAccountDTO dto)
        {
            try
            {
                var account = await _ledgerService.CreateAccountAsync(dto);
                return Ok(new AccountDTO { Id = account.Id, Code = account.Code, Name = account.Name, Type = account.Type });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Create account failed for code {AccountCode}.", dto.Code);
                return BadRequest(new { error = "Unable to create account. Please verify the account details and try again." });
            }
        }

        [HttpPut("accounts/{id}")]
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> UpdateAccount(int id, [FromBody] UpdateAccountDTO dto)
        {
            try
            {
                await _ledgerService.UpdateAccountAsync(id, dto);
                return Ok(new { message = "Account updated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update account failed for account ID {AccountId}.", id);
                return BadRequest(new { error = "Unable to update account. Please verify the account details and try again." });
            }
        }

        [HttpDelete("accounts/{id}")]
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> DeleteAccount(int id)
        {
            try
            {
                await _ledgerService.DeleteAccountAsync(id);
                return Ok(new { message = "Account archived" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Archive account failed for account ID {AccountId}.", id);
                return BadRequest(new { error = "Unable to archive account. Please verify the account state and try again." });
            }
        }

        [HttpPut("accounts/{id}/restore")]
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> RestoreAccount(int id)
        {
            try
            {
                await _ledgerService.RestoreAccountAsync(id);
                return Ok(new { message = "Account restored" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore account failed for account ID {AccountId}.", id);
                return BadRequest(new { error = "Unable to restore account. Please verify the account state and try again." });
            }
        }

        [HttpGet("trial-balance")]
        [Authorize(Roles = "Admin,Accounting,Management")]
        public async Task<IActionResult> GetTrialBalance(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] bool excludeClosingEntries = false)
        {
            if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
                return BadRequest(new { error = "fromDate cannot be later than toDate." });

            var tb = await _ledgerService.GetTrialBalanceAsync(fromDate, toDate, excludeClosingEntries);
            return Ok(tb);
        }

        [HttpGet("fiscal-years")]
        [Authorize(Roles = "Admin,Accounting,Management")]
        public async Task<IActionResult> GetFiscalYears([FromQuery] int lookbackYears = 10)
        {
            var years = await _yearEndCloseService.GetFiscalYearSummariesAsync(lookbackYears);
            return Ok(years);
        }

        [HttpPost("fiscal-years/{fiscalYear:int}/close")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CloseFiscalYear(int fiscalYear)
        {
            var userName = User.Identity?.Name ?? "System";
            try
            {
                var result = await _yearEndCloseService.CloseFiscalYearAsync(fiscalYear, userName);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Fiscal year close validation failed for fiscal year {FiscalYear}.", fiscalYear);

                if (ex.Message.Contains("already closed", StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { error = "Fiscal year is already closed." });

                return BadRequest(new { error = "Unable to close fiscal year. Verify the fiscal year is complete and has activity, then try again." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while closing fiscal year {FiscalYear}.", fiscalYear);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred. Please try again later." });
            }
        }

        [HttpPost("journal")]
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> PostJournalEntry([FromBody] JournalEntryDTO entryDto)
        {
            string userId = User.Identity?.Name ?? "Unknown";
            try
            {
                var entry = await _ledgerService.CreateJournalEntryAsync(entryDto, userId);
                return Ok(entry);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Journal entry post validation failed.");
                return BadRequest(new { error = "Unable to post journal entry. Ensure the entry is balanced and the posting period is open." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while posting journal entry.");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred. Please try again later." });
            }
        }
    }
}
