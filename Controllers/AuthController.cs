using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureFileHub.Data;
using SecureFileHub.Models;
using SecureFileHub.Services;
using SecureFileHub.ViewModels;

namespace SecureFileHub.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AuditService _audit;
        private readonly IConfiguration _config;

        public AuthController(AppDbContext db, AuditService audit, IConfiguration config)
        {
            _db = db;
            _audit = audit;
            _config = config;
        }

        // GET /Auth/Register — show the empty form
        [HttpGet]
        public IActionResult Register()
        {
            // If already logged in, go to dashboard
            if (HttpContext.Session.GetString("UserId") != null)
                return RedirectToAction("Index", "Home");

            return View();
        }

        // POST /Auth/Register — process the form
        [HttpPost]
        [ValidateAntiForgeryToken] // prevents CSRF attacks on forms
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            // Step 1: Check all the [Required], [EmailAddress], [RegularExpression]
            // rules on the ViewModel — if anything fails, go back to the form
            if (!ModelState.IsValid)
                return View(model);

            // Step 2: Check if email already exists
            // EF Core uses a parameterized query here automatically — no SQL injection possible
            var existingUser = await _db.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email.ToLower().Trim());

            if (existingUser != null)
            {
                // Vague message on purpose — don't reveal which emails are registered
                ModelState.AddModelError("Email", "An account with this email already exists");
                return View(model);
            }

            // Step 3: Hash the password with BCrypt
            // WorkFactor 12 means 2^12 = 4096 hashing rounds — slow enough to resist brute force
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(model.Password, workFactor: 12);

            // Step 4: Create the user — never store the raw password
            var user = new User
            {
                Email = model.Email.ToLower().Trim(),
                PasswordHash = passwordHash,
                Role = "User",
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Step 5: Log the event (who, what, when, from where)
            await _audit.LogAsync("REGISTER_SUCCESS", user.Id.ToString(), $"New user registered: {user.Email}");

            // Step 6: Redirect to login with a success message
            TempData["Success"] = "Account created! Please log in.";
            return RedirectToAction("Login");
        }

        // GET /Auth/Login — show the login form
        [HttpGet]
        public IActionResult Login()
        {
            // Already logged in? Go to dashboard
            if (HttpContext.Session.GetString("UserId") != null)
                return RedirectToAction("Index", "Home");

            return View();
        }

        // POST /Auth/Login — process the form
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Read config values from appsettings.json
            var maxAttempts = _config.GetValue<int>("AppSettings:MaxLoginAttempts");
            var lockoutMinutes = _config.GetValue<int>("AppSettings:LockoutDurationMinutes");

            // Step 1: Find the user by email
            // EF Core parameterizes this automatically — no SQL injection possible
            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email.ToLower().Trim());

            // Step 2: User not found
            // Return the SAME message as wrong password — never reveal which is wrong
            if (user == null)
            {
                await _audit.LogAsync("LOGIN_FAILED_USER_NOT_FOUND", null, $"Email attempted: {model.Email}");
                ModelState.AddModelError("", "Invalid email or password");
                return View(model);
            }

            // Step 3: Check if account is locked out
            if (user.LockoutUntil.HasValue && user.LockoutUntil > DateTime.UtcNow)
            {
                var remaining = (user.LockoutUntil.Value - DateTime.UtcNow).Minutes + 1;
                await _audit.LogAsync("LOGIN_BLOCKED_LOCKOUT", user.Id.ToString(), $"Account locked, {remaining} min remaining");
                ModelState.AddModelError("", $"Account is locked. Try again in {remaining} minute(s).");
                return View(model);
            }

            // Step 4: Verify the password against the bcrypt hash
            var passwordValid = BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash);

            if (!passwordValid)
            {
                // Increment failed attempts
                user.FailedLoginAttempts++;

                // Lock the account if max attempts reached
                if (user.FailedLoginAttempts >= maxAttempts)
                {
                    user.LockoutUntil = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                    await _audit.LogAsync("LOGIN_ACCOUNT_LOCKED", user.Id.ToString(),
                        $"Locked after {user.FailedLoginAttempts} failed attempts");
                    ModelState.AddModelError("", $"Too many failed attempts. Account locked for {lockoutMinutes} minute(s).");
                }
                else
                {
                    var attemptsLeft = maxAttempts - user.FailedLoginAttempts;
                    await _audit.LogAsync("LOGIN_FAILED_WRONG_PASSWORD", user.Id.ToString(),
                        $"Wrong password, {attemptsLeft} attempts remaining");
                    ModelState.AddModelError("", $"Invalid email or password. {attemptsLeft} attempt(s) remaining.");
                }

                await _db.SaveChangesAsync();
                return View(model);
            }

            // Step 5: Successful login — reset failed attempts
            user.FailedLoginAttempts = 0;
            user.LockoutUntil = null;
            await _db.SaveChangesAsync();

            // Step 6: Store user info in session
            // These are the three keys the navbar reads
            HttpContext.Session.SetString("UserId", user.Id.ToString());
            HttpContext.Session.SetString("UserEmail", user.Email);
            HttpContext.Session.SetString("UserRole", user.Role);

            await _audit.LogAsync("LOGIN_SUCCESS", user.Id.ToString(), $"User logged in: {user.Email}");

            TempData["Success"] = $"Welcome back, {user.Email}!";
            return RedirectToAction("Index", "Home");
        }

        // POST /Auth/Logout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var userId = HttpContext.Session.GetString("UserId");
            var email = HttpContext.Session.GetString("UserEmail");

            // Clear the entire session server-side — token is now invalid
            HttpContext.Session.Clear();

            await _audit.LogAsync("LOGOUT", userId, $"User logged out: {email}");

            TempData["Success"] = "You have been signed out.";
            return RedirectToAction("Login");
        }
    }
}
