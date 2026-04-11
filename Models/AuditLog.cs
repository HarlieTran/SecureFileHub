namespace SecureFileHub.Models
{
    public class AuditLog
    {
        public int Id { get; set; }
        public string Event { get; set; } = string.Empty;   
        public string? UserId { get; set; }                  
        public string IpAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Details { get; set; }                 
    }
}
