using System.Security.Claims;
using IrrigacaoConfig.Models;
using IrrigacaoConfig.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace IrrigacaoConfig.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        // Cadastro = criar conta + reivindicar o projeto (vira dono). Precisa do código do projeto.
        g.MapPost("/register", async (RegisterRequest req, IAppStore store, ProjectRegistry registry,
            HttpContext http, CancellationToken ct) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 80) errors["name"] = ["Informe seu nome (até 80 caracteres)."];
            if (!SettingsValidator.IsValidEmail(email)) errors["email"] = ["E-mail inválido."];
            if (req.Password is null || req.Password.Length < 8 || req.Password.Length > 128) errors["password"] = ["A senha precisa ter de 8 a 128 caracteres."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var project = registry.Find(req.ProjectId?.Trim() ?? "");
            if (project is null || !PasswordHasher.SecretEquals(req.ClaimCode?.Trim(), project.ClaimCode))
                return Problem(400, "Projeto ou código de ativação inválido.");
            if (await store.GetSettingsAsync(project.ProjectId, ct) is not null)
                return Problem(409, "Este projeto já tem um dono cadastrado.");

            var user = new AppUser { Name = req.Name.Trim(), Email = email, PasswordHash = PasswordHasher.Hash(req.Password!) };
            if (!await store.TryInsertUserAsync(user, ct))
                return Problem(409, "Já existe uma conta com este e-mail. Entre e use \"Adicionar projeto\".");

            var settings = new ProjectSettings { ProjectId = project.ProjectId, OwnerUserId = user.Id, AlertEmail = email };
            if (!await store.TryInsertSettingsAsync(settings, ct))
            {
                await store.DeleteUserAsync(user.Id, ct); // outro cadastro ganhou a corrida
                return Problem(409, "Este projeto já tem um dono cadastrado.");
            }

            await SignInAsync(http, user);
            return Results.Ok(new MeResponse(user.Id, user.Name, user.Email, [project.ProjectId]));
        }).RequireRateLimiting("auth");

        g.MapPost("/login", async (LoginRequest req, IAppStore store, HttpContext http, CancellationToken ct) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            var user = await store.FindUserByEmailAsync(email, ct);
            if (user is null || req.Password is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Problem(401, "E-mail ou senha incorretos.");

            await SignInAsync(http, user);
            var projects = await store.GetSettingsByOwnerAsync(user.Id, ct);
            return Results.Ok(new MeResponse(user.Id, user.Name, user.Email, projects.Select(p => p.ProjectId).ToList()));
        }).RequireRateLimiting("auth");

        g.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        g.MapGet("/me", async (ClaimsPrincipal principal, IAppStore store, CancellationToken ct) =>
        {
            var user = await store.FindUserByIdAsync(principal.UserId(), ct);
            if (user is null) return Results.Unauthorized();
            var projects = await store.GetSettingsByOwnerAsync(user.Id, ct);
            return Results.Ok(new MeResponse(user.Id, user.Name, user.Email, projects.Select(p => p.ProjectId).ToList()));
        }).RequireAuthorization();
    }

    private static async Task SignInAsync(HttpContext http, AppUser user)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email)
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    public static IResult Problem(int status, string detail) => Results.Problem(detail: detail, statusCode: status);

    public static string UserId(this ClaimsPrincipal p) => p.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
}
