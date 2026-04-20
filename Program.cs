using Microsoft.EntityFrameworkCore;
using SecureFileHub.Data;
using SecureFileHub.Models;
using SecureFileHub.Services;

namespace SecureFileHub
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Load .env for secrets that can't go in appsettings.json
            if (File.Exists(".env"))
                DotNetEnv.Env.Load();

            var encryptionKey = Environment.GetEnvironmentVariable("ENCRYPTION_KEY");

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            // SQL Server connection
            builder.Services.AddDbContext<AppDbContext>(
                options => options.UseSqlite(
                    builder.Configuration.GetConnectionString("DefaultConnection")
                    )
                );

            // Session support (for login state)
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(
                    builder.Configuration.GetValue<int>("AppSettings:SessionTimeoutMinutes"));
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
            });

            builder.Services.AddHttpContextAccessor();

            builder.Services.AddScoped<AuditService>();
            builder.Services.AddSingleton<EncryptionService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self'; " +
                    "style-src 'self' 'unsafe-inline'; " +
                    "img-src 'self' data:; " +
                    "frame-ancestors 'none';";
                context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                await next();
            });

            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            // Seed default users for testing
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();

                if (!db.Users.Any())
                {
                    db.Users.AddRange(
                        new User
                        {
                            Email = "admin@test.com",
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123", workFactor: 12),
                            Role = "Admin",
                            CreatedAt = DateTime.UtcNow
                        },
                        new User
                        {
                            Email = "user@test.com",
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword("User@1234", workFactor: 12),
                            Role = "User",
                            CreatedAt = DateTime.UtcNow
                        }
                    );
                    db.SaveChanges();
                }
            }

            app.Run();
        }
    }
}
