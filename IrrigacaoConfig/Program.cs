using System.Security.Claims;
using System.Threading.RateLimiting;
using IrrigacaoConfig.Endpoints;
using IrrigacaoConfig.Models;
using IrrigacaoConfig.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));
builder.Services.Configure<OrionOptions>(builder.Configuration.GetSection("Orion"));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));

builder.Services.AddSingleton<ProjectRegistry>();
var storage = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new();
if (storage.Provider.Equals("Memory", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IAppStore, InMemoryAppStore>();
else
    builder.Services.AddSingleton<IAppStore, MongoAppStore>();

var geminiTimeout = builder.Configuration.GetValue("Gemini:TimeoutSeconds", 45);
builder.Services.AddHttpClient<GeminiPlantAdvisor>(c => c.Timeout = TimeSpan.FromSeconds(geminiTimeout));
builder.Services.AddHttpClient<OrionSyncService>(c => c.Timeout = TimeSpan.FromSeconds(8));

// Chaves que assinam o cookie de login: persistidas para o login sobreviver a reinícios do container.
builder.Services.AddDataProtection()
    .SetApplicationName("IrrigacaoConfig")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeysPath"] ?? "keys"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "irrig.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;               // protege contra CSRF
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // funciona em HTTP no EC2; use HTTPS se puder
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
        // API: responde 401/403 em vez de redirecionar para uma página de login.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "?";
    // Login/cadastro: 10 tentativas por minuto por IP.
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    // IA: 8 chamadas por minuto por usuário (controla custo do Gemini).
    o.AddPolicy("ai", c => RateLimitPartition.GetFixedWindowLimiter(
        c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Ip(c),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1) }));
});

var dashboardOrigins = builder.Configuration.GetSection("Security:DashboardOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddPolicy("dashboard", p =>
{
    if (dashboardOrigins.Length > 0) p.WithOrigins(dashboardOrigins).WithMethods("GET");
}));

builder.Services.AddProblemDetails();

var app = builder.Build();

await app.Services.GetRequiredService<IAppStore>().EnsureIndexesAsync(CancellationToken.None);

if (string.IsNullOrWhiteSpace(app.Configuration["Security:ServiceApiKey"]))
    app.Logger.LogWarning("Security:ServiceApiKey vazio: o endpoint interno vai recusar todas as chamadas.");
if (string.IsNullOrWhiteSpace(app.Configuration["Gemini:ApiKey"]))
    app.Logger.LogWarning("Gemini:ApiKey vazio: a identificação de plantas não vai funcionar.");

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapProjectEndpoints();

app.Run();
