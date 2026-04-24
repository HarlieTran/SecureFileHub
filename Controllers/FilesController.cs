using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureFileHub.Data;
using SecureFileHub.Models;
using SecureFileHub.Services;

namespace SecureFileHub.Controllers
{
    public class FilesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuditService _audit;
        private readonly IWebHostEnvironment _env;

        private static readonly HashSet<string> AllowedExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".png", ".jpg", ".txt" };

        // Magic bytes for MIME validation
        private static readonly Dictionary<string, byte[]> MagicBytes = new()
        {
            { ".pdf",  new byte[] { 0x25, 0x50, 0x44, 0x46 } },         // %PDF
            { ".png",  new byte[] { 0x89, 0x50, 0x4E, 0x47 } },         // PNG
            { ".jpg",  new byte[] { 0xFF, 0xD8, 0xFF } },                // JPEG
            { ".docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 } },         // ZIP (docx)
            { ".txt",  Array.Empty<byte>() },                            // no magic bytes for txt
        };

        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

        private readonly EncryptionService _encryption;

        public FilesController(AppDbContext db, AuditService audit, IWebHostEnvironment env, EncryptionService encryption)
        {
            _db = db;
            _audit = audit;
            _env = env;
            _encryption = encryption;
        }

        private string? SessionUserId => HttpContext.Session.GetString("UserId");
        private string? SessionRole => HttpContext.Session.GetString("UserRole");

        // GET /Files
        public async Task<IActionResult> Index()
        {
            if (SessionUserId == null) return RedirectToAction("Login", "Auth");

            IQueryable<FileRecord> query = _db.FileRecords.Include(f => f.User);

            if (SessionRole != "Admin")
                query = query.Where(f => f.UserId.ToString() == SessionUserId);

            var files = await query.OrderByDescending(f => f.UploadedAt).ToListAsync();
            return View(files);
        }

        // GET /Files/Upload
        [HttpGet]
        public IActionResult Upload()
        {
            if (SessionUserId == null) return RedirectToAction("Login", "Auth");
            return View();
        }

        // POST /Files/Upload
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(104_857_600)] // 100MB — controller enforces the real 10MB limit
        [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (SessionUserId == null) return RedirectToAction("Login", "Auth");

            if (file == null)
            {
                ModelState.AddModelError("", "Please select a file.");
                return View();
            }

            if (file.Length == 0)
            {
                ModelState.AddModelError("", "The selected file is empty.");
                return View();
            }

            // 1. File size check
            if (file.Length > MaxFileSizeBytes)
            {
                ModelState.AddModelError("", "File exceeds the 10 MB limit.");
                return View();
            }

            // 2. Extension allowlist
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
            {
                ModelState.AddModelError("", $"File type '{ext}' is not allowed. Allowed: .pdf .docx .png .jpg .txt");
                return View();
            }

            // 3. Magic bytes validation (MIME spoofing prevention)
            if (MagicBytes.TryGetValue(ext, out var magic) && magic.Length > 0)
            {
                var header = new byte[magic.Length];
                await file.OpenReadStream().ReadExactlyAsync(header);
                if (!header.SequenceEqual(magic))
                {
                    await _audit.LogAsync("FILE_UPLOAD_REJECTED_MIME", SessionUserId, $"MIME mismatch for: {file.FileName}");
                    ModelState.AddModelError("", "File content does not match its extension.");
                    return View();
                }
            }

            // 4. Sanitize filename — store as UUID, never use original name on disk
            var storedName = $"{Guid.NewGuid():N}.bin";
            var uploadDir = Path.Combine(_env.ContentRootPath, "uploads");
            Directory.CreateDirectory(uploadDir);
            var filePath = Path.Combine(uploadDir, storedName);

            var encryptedBytes = await _encryption.EncryptStreamAsync(file.OpenReadStream());
            await System.IO.File.WriteAllBytesAsync(filePath, encryptedBytes);

            var record = new FileRecord
            {
                UserId = int.Parse(SessionUserId!),
                OriginalName = Path.GetFileName(file.FileName), // strip any path components
                StoredName = storedName,
                ContentType = file.ContentType,
                FileSizeBytes = file.Length,
                UploadedAt = DateTime.UtcNow
            };

            _db.FileRecords.Add(record);
            await _db.SaveChangesAsync();

            await _audit.LogAsync("FILE_UPLOAD", SessionUserId, $"Uploaded: {record.OriginalName} ({file.Length} bytes)");

            TempData["Success"] = $"File '{record.OriginalName}' uploaded successfully.";
            return RedirectToAction("Index");
        }

        // GET /Files/Download/{id}
        [HttpGet]
        public async Task<IActionResult> Download(int id)
        {
            if (SessionUserId == null) return RedirectToAction("Login", "Auth");

            var record = await _db.FileRecords.FindAsync(id);
            if (record == null) return NotFound();

            // IDOR check — only owner or admin can download
            if (record.UserId.ToString() != SessionUserId && SessionRole != "Admin")
            {
                await _audit.LogAsync("FILE_ACCESS_DENIED", SessionUserId, $"Attempted download of file {id}");
                return StatusCode(403, "Access denied. You do not have permission to access this file.");
            }

            var filePath = Path.Combine(_env.ContentRootPath, "uploads", record.StoredName);
            if (!System.IO.File.Exists(filePath)) return NotFound();

            await _audit.LogAsync("FILE_DOWNLOAD", SessionUserId, $"Downloaded: {record.OriginalName}");

            var plaintext = _encryption.DecryptFile(filePath);
            return File(plaintext, record.ContentType, record.OriginalName);
        }

        // POST /Files/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (SessionUserId == null) return RedirectToAction("Login", "Auth");

            var record = await _db.FileRecords.Include(f => f.ShareLinks).FirstOrDefaultAsync(f => f.Id == id);
            if (record == null) return NotFound();

            // IDOR check
            if (record.UserId.ToString() != SessionUserId && SessionRole != "Admin")
            {
                await _audit.LogAsync("FILE_DELETE_DENIED", SessionUserId, $"Attempted delete of file {id}");
                return StatusCode(403, "Access denied. You do not have permission to delete this file.");
            }

            // Delete file from disk
            var filePath = Path.Combine(_env.ContentRootPath, "uploads", record.StoredName);
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);

            // Remove share links then file record
            _db.ShareLinks.RemoveRange(record.ShareLinks);
            _db.FileRecords.Remove(record);
            await _db.SaveChangesAsync();

            await _audit.LogAsync("FILE_DELETE", SessionUserId, $"Deleted: {record.OriginalName}");

            TempData["Success"] = $"File '{record.OriginalName}' deleted.";
            return RedirectToAction("Index");
        }

        // POST /Files/Share/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Share(int id, string permission, int expiryHours, string? password)
        {
            if (SessionUserId == null) return RedirectToAction("Login", "Auth");

            var record = await _db.FileRecords.FindAsync(id);
            if (record == null) return NotFound();

            // IDOR check
            if (record.UserId.ToString() != SessionUserId && SessionRole != "Admin")
                return StatusCode(403, "Access denied. You do not have permission to share this file.");

            var link = new ShareLink
            {
                FileId = id,
                Token = Guid.NewGuid().ToString("N"),
                Permission = permission == "Download" ? "Download" : "View",
                ExpiresAt = DateTime.UtcNow.AddHours(expiryHours > 0 ? expiryHours : 24),
                PasswordHash = string.IsNullOrWhiteSpace(password)
                    ? null
                    : BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
                CreatedAt = DateTime.UtcNow
            };

            _db.ShareLinks.Add(link);
            await _db.SaveChangesAsync();

            await _audit.LogAsync("FILE_SHARE_CREATED", SessionUserId, $"Share link created for file {id}, permission: {link.Permission}");

            TempData["ShareUrl"] = Url.Action("AccessShare", "Files", new { token = link.Token }, Request.Scheme);
            return RedirectToAction("Index");
        }

        // GET /Files/AccessShare/{token}
        [HttpGet]
        public async Task<IActionResult> AccessShare(string token)
        {
            var link = await _db.ShareLinks.Include(s => s.File).FirstOrDefaultAsync(s => s.Token == token);

            if (link == null) return NotFound("Share link not found.");
            if (link.ExpiresAt < DateTime.UtcNow) return BadRequest("This share link has expired.");

            // If password protected, show password form
            if (link.PasswordHash != null)
                return View("SharePassword", link);

            return await ServeSharedFile(link);
        }

        // POST /Files/AccessShare/{token}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AccessShare(string token, string password)
        {
            var link = await _db.ShareLinks.Include(s => s.File).FirstOrDefaultAsync(s => s.Token == token);

            if (link == null) return NotFound("Share link not found.");
            if (link.ExpiresAt < DateTime.UtcNow) return BadRequest("This share link has expired.");

            if (link.PasswordHash != null && !BCrypt.Net.BCrypt.Verify(password, link.PasswordHash))
            {
                ViewBag.Error = "Incorrect password.";
                return View("SharePassword", link);
            }

            return await ServeSharedFile(link);
        }

        private async Task<IActionResult> ServeSharedFile(ShareLink link)
        {
            var filePath = Path.Combine(_env.ContentRootPath, "uploads", link.File.StoredName);
            if (!System.IO.File.Exists(filePath)) return NotFound("File no longer exists.");

            await _audit.LogAsync("FILE_SHARE_ACCESSED", null, $"Share token accessed for file {link.FileId}");

            if (link.Permission == "Download")
            {
                var plaintext = _encryption.DecryptFile(filePath);
                return File(plaintext, link.File.ContentType, link.File.OriginalName);
            }

            // View-only: show file info page
            return View("ShareView", link);
        }
    }
}
