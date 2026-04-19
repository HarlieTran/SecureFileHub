using Microsoft.EntityFrameworkCore;
using SecureFileHub.Data;
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

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            app.Run();
        }
    }
}
