using System.ComponentModel.DataAnnotations;

namespace SecureFileHub.Models
{
    public class FileRecord
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        [Required, MaxLength(512)]
        public string OriginalName { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string StoredName { get; set; } = string.Empty;

        [Required, MaxLength(128)]
        public string ContentType { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public User User { get; set; } = null!;

        public ICollection<ShareLink> ShareLinks { get; set; } = new List<ShareLink>();
    }
}
