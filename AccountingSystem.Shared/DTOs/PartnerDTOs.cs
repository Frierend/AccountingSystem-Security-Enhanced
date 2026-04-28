using System.ComponentModel.DataAnnotations;

namespace AccountingSystem.Shared.DTOs
{
    // --- VENDORS ---
    public class VendorDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class CreateVendorDTO
    {
        [Required]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
        public string Name { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
        public string Email { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Contact person cannot exceed 100 characters.")]
        public string ContactPerson { get; set; } = string.Empty;
    }

    public class UpdateVendorDTO : CreateVendorDTO
    {
        public int Id { get; set; }
    }

    // --- CUSTOMERS ---
    public class CustomerDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class CreateCustomerDTO
    {
        [Required]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
        public string Name { get; set; } = string.Empty;

        [EmailAddress]
        [StringLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
        public string Email { get; set; } = string.Empty;

        [StringLength(30, ErrorMessage = "Phone cannot exceed 30 characters.")]
        public string Phone { get; set; } = string.Empty;
    }

    public class UpdateCustomerDTO : CreateCustomerDTO
    {
        public int Id { get; set; }
    }
}
