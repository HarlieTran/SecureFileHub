using System.ComponentModel.DataAnnotations;

namespace SecureFileHub.Models
{
    public class ShareLink
    {
        public int Id { get; set; }

        public int FileId { get; set; }

        [Required, MaxLength(64)]
        public string Token { get; set; } = string.Empty;

        [Required, MaxLength(16)]
        public string Permission { get; set; } = "View"; // "View" | "Download"

        public DateTime ExpiresAt { get; set; }

        public string? PasswordHash { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public FileRecord File { get; set; } = null!;
    }
}
