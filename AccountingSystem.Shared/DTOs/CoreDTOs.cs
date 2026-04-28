using AccountingSystem.Shared.Enums;
using System.ComponentModel.DataAnnotations;

namespace AccountingSystem.Shared.DTOs
{
    // --- LEDGER ---
    public class JournalEntryDTO
    {
        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string Description { get; set; } = string.Empty;

        public DateTime Date { get; set; }

        [Required]
        [MinLength(2, ErrorMessage = "At least two journal lines are required.")]
        public List<JournalEntryLineDTO> Lines { get; set; } = new();
    }

    public class JournalEntryLineDTO
    {
        [Range(1, int.MaxValue, ErrorMessage = "Please select an account.")]
        public int AccountId { get; set; }

        [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Debit cannot be negative.")]
        public decimal Debit { get; set; }

        [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "Credit cannot be negative.")]
        public decimal Credit { get; set; }
    }

    public class TrialBalanceDTO
    {
        public DateTime GeneratedAt { get; set; }
        public List<AccountBalanceDTO> Accounts { get; set; } = new();
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
    }

    public class AccountBalanceDTO
    {
        public string AccountName { get; set; } = string.Empty;
        public string AccountCode { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    // --- PAYABLES ---
    public class BillDTO
    {
        public int Id { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public string VendorReferenceNumber { get; set; } = string.Empty;
        public string SystemReferenceNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal AmountPaid { get; set; }
        public DocumentStatus Status { get; set; } // Enum
        public decimal Balance => Amount - AmountPaid;
    }

    public class CreateBillDTO
    {
        [Range(1, int.MaxValue, ErrorMessage = "Please select a vendor.")]
        public int VendorId { get; set; }

        public DateTime DueDate { get; set; }

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Amount must be greater than 0.")]
        public decimal Amount { get; set; }

        [StringLength(100, ErrorMessage = "Vendor reference number cannot exceed 100 characters.")]
        public string VendorReferenceNumber { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string Description { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Please select an expense account.")]
        public int ExpenseAccountId { get; set; }
    }

    // --- RECEIVABLES ---
    public class InvoiceDTO
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal PaidAmount { get; set; }
        public DocumentStatus Status { get; set; } // Enum
        public decimal Balance => TotalAmount - PaidAmount;
    }


    public class DocumentCreationResultDTO
    {
        public int Id { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;
    }

    public class PaymentCreationResultDTO
    {
        public int Id { get; set; }
        public string ReferenceNumber { get; set; } = string.Empty;
    }

    public class CreateInvoiceDTO
    {
        [Range(1, int.MaxValue, ErrorMessage = "Please select a customer.")]
        public int CustomerId { get; set; }

        public DateTime DueDate { get; set; }

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Amount must be greater than 0.")]
        public decimal Amount { get; set; }

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string Description { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Please select a revenue account.")]
        public int RevenueAccountId { get; set; }
    }
}
