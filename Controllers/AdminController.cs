using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureFileHub.Data;
using SecureFileHub.Services;

namespace SecureFileHub.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuditService _audit;

        public AdminController(AppDbContext db, AuditService audit)
        {
            _db = db;
            _audit = audit;
        }

        private bool IsAdmin =>
            HttpContext.Session.GetString("UserId") != null &&
            HttpContext.Session.GetString("UserRole") == "Admin";

        // GET /Admin
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin) return StatusCode(403, "Access denied.");

            var model = new AdminViewModel
            {
                AuditLogs = await _db.AuditLogs
                    .OrderByDescending(a => a.Timestamp)
                    .Take(200)
                    .ToListAsync(),
                Users = await _db.Users.OrderBy(u => u.Email).ToListAsync(),
                Files = await _db.FileRecords.Include(f => f.User)
                    .OrderByDescending(f => f.UploadedAt)
                    .ToListAsync()
            };

            return View(model);
        }

        // POST /Admin/DeleteUser/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            if (!IsAdmin) return StatusCode(403, "Access denied.");

            var user = await _db.Users
                .Include(u => u.FileRecords)
                    .ThenInclude(f => f.ShareLinks)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null) return NotFound();

            var adminUserId = HttpContext.Session.GetString("UserId");

            // Delete files from disk
            foreach (var file in user.FileRecords)
            {
                // will be injected via DI in a real scenario; use IWebHostEnvironment
                var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "uploads", file.StoredName);
                if (System.IO.File.Exists(uploadsPath))
                    System.IO.File.Delete(uploadsPath);
            }

            _db.Users.Remove(user); // cascade deletes FileRecords and ShareLinks
            await _db.SaveChangesAsync();

            await _audit.LogAsync("ADMIN_USER_DELETED", adminUserId, $"Deleted user id={id}");

            TempData["Success"] = "User and all their files have been deleted.";
            return RedirectToAction("Index");
        }
    }

    public class AdminViewModel
    {
        public List<Models.AuditLog> AuditLogs { get; set; } = new();
        public List<Models.User> Users { get; set; } = new();
        public List<Models.FileRecord> Files { get; set; } = new();
    }
}
