using System.ComponentModel.DataAnnotations;

namespace SecureFileHub.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        // NEVER store plaintext — this stores the bcrypt hash
        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string Role { get; set; } = "User"; // "User" or "Admin"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Brute-force protection fields
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutUntil { get; set; }

        public ICollection<FileRecord> FileRecords { get; set; } = new List<FileRecord>();
    }
}
