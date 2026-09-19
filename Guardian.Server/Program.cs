using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Guardian.Core.Services;
using Guardian.Server.Controllers;
using Guardian.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add Services to Container
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configure JWT Bearer Authentication
var key = Encoding.UTF8.GetBytes(AuthController.JwtSecret);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
});

// Register Core Guardian Services as Singletons
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
builder.Services.AddSingleton<IUsageTrackingService, UsageTrackingService>();
builder.Services.AddSingleton<IEnforcementService, EnforcementService>();
builder.Services.AddSingleton<IDeviceSyncService, DeviceSyncService>();

var app = builder.Build();

// Enable Static Files (wwwroot for Parent Dashboard UI)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GuardianHub>("/hubs/guardian");

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

Logger.Info("Guardian Server starting up on HTTP/HTTPS...");

app.Run();
