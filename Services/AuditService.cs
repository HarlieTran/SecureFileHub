using SecureFileHub.Data;
using SecureFileHub.Models;

namespace SecureFileHub.Services
{
    public class AuditService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(string eventName, string? userId = null, string? details = null)
        {
            // Get the real IP address of the request
            var context = _httpContextAccessor.HttpContext;
            var ip = context?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // IMPORTANT: never log passwords, file contents, or encryption keys
            var entry = new AuditLog
            {
                Event = eventName,
                UserId = userId,
                IpAddress = ip,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync();
        }
    }
}
