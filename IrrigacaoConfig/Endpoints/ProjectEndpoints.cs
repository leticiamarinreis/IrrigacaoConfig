using System.Security.Claims;
using IrrigacaoConfig.Models;
using IrrigacaoConfig.Services;
using Microsoft.Extensions.Options;
using static IrrigacaoConfig.Endpoints.AuthEndpoints;

namespace IrrigacaoConfig.Endpoints;

public static class ProjectEndpoints
{
    private const long MaxPhotoBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> AllowedImageTypes = ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/projects").RequireAuthorization().WithTags("Projetos");

        // Usuário já logado reivindica outro projeto (ex.: irrig002).
        g.MapPost("/claim", async (ClaimProjectRequest req, ClaimsPrincipal user, IAppStore store,
            ProjectRegistry registry, CancellationToken ct) =>
        {
            var project = registry.Find(req.ProjectId?.Trim() ?? "");
            if (project is null || !PasswordHasher.SecretEquals(req.ClaimCode?.Trim(), project.ClaimCode))
                return Problem(400, "Projeto ou código de ativação inválido.");
            var me = await store.FindUserByIdAsync(user.UserId(), ct);
            if (me is null) return Results.Unauthorized();
            var settings = new ProjectSettings { ProjectId = project.ProjectId, OwnerUserId = me.Id, AlertEmail = me.Email };
            return await store.TryInsertSettingsAsync(settings, ct)
                ? Results.Ok(settings)
                : Problem(409, "Este projeto já tem um dono cadastrado.");
        }).RequireRateLimiting("auth");

        g.MapGet("/{projectId}/settings", async (string projectId, ClaimsPrincipal user, IAppStore store, CancellationToken ct) =>
            await LoadOwnedAsync(projectId, user, store, ct) is { } s ? Results.Ok(s) : NotFound());

        g.MapPut("/{projectId}/settings", async (string projectId, SettingsUpdateRequest req, ClaimsPrincipal user,
            IAppStore store, ProjectRegistry registry, OrionSyncService orion, CancellationToken ct) =>
        {
            var s = await LoadOwnedAsync(projectId, user, store, ct);
            if (s is null) return NotFound();
            var errors = SettingsValidator.Validate(req);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            s.AlertEmail = req.AlertEmail.Trim();
            s.ReservoirAlertDistanceCm = req.ReservoirAlertDistanceCm;
            s.Thresholds = req.Thresholds;
            s.CustomVariables = req.CustomVariables ?? new();
            return Results.Ok(await SaveAndSyncAsync(s, store, registry, orion, ct));
        });

        // 1) Foto → IA identifica e sugere faixas. Não salva nada.
        g.MapPost("/{projectId}/plant/identify", async (string projectId, IFormFile photo, ClaimsPrincipal user,
            IAppStore store, GeminiPlantAdvisor advisor, CancellationToken ct) =>
        {
            var s = await LoadOwnedAsync(projectId, user, store, ct);
            if (s is null) return NotFound();
            if (photo is null || photo.Length == 0) return Problem(400, "Envie uma foto.");
            if (photo.Length > MaxPhotoBytes) return Problem(413, "A foto deve ter no máximo 8 MB.");
            var mime = (photo.ContentType ?? "").ToLowerInvariant();
            if (!AllowedImageTypes.Contains(mime)) return Problem(415, "Formato não suportado. Use JPG, PNG, WEBP ou HEIC.");

            using var ms = new MemoryStream();
            await photo.CopyToAsync(ms, ct);
            try { return Results.Ok(await advisor.IdentifyFromPhotoAsync(ms.ToArray(), mime, s.Thresholds.LuminosityUnit, ct)); }
            catch (PlantAdvisorException ex) { return Problem(502, ex.Message); }
        }).DisableAntiforgery().RequireRateLimiting("ai");

        // 1b) Alternativa sem foto: o dono digita o nome.
        g.MapPost("/{projectId}/plant/suggest", async (string projectId, PlantByNameRequest req, ClaimsPrincipal user,
            IAppStore store, GeminiPlantAdvisor advisor, CancellationToken ct) =>
        {
            var s = await LoadOwnedAsync(projectId, user, store, ct);
            if (s is null) return NotFound();
            var name = req.PlantName?.Trim() ?? "";
            if (name.Length is < 2 or > 80) return Problem(400, "Digite o nome da planta (2 a 80 caracteres).");
            try { return Results.Ok(await advisor.SuggestFromNameAsync(name, s.Thresholds.LuminosityUnit, ct)); }
            catch (PlantAdvisorException ex) { return Problem(502, ex.Message); }
        }).RequireRateLimiting("ai");

        // 2) Dono confirma (pode ter editado) → salva planta + faixas e sincroniza.
        g.MapPost("/{projectId}/plant/apply", async (string projectId, PlantApplyRequest req, ClaimsPrincipal user,
            IAppStore store, ProjectRegistry registry, OrionSyncService orion, CancellationToken ct) =>
        {
            var s = await LoadOwnedAsync(projectId, user, store, ct);
            if (s is null) return NotFound();

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.CommonName) || req.CommonName.Length > 80) errors["commonName"] = ["Informe o nome da planta."];
            if (req.Source is not ("ia-foto" or "ia-nome" or "manual")) errors["source"] = ["Origem inválida."];
            SettingsValidator.ValidateThresholds(req.Thresholds, (k, m) => errors[k] = [m]);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            s.Plant = new PlantProfile
            {
                CommonName = req.CommonName.Trim(),
                ScientificName = (req.ScientificName ?? "").Trim(),
                Confidence = Math.Clamp(req.Confidence, 0, 1),
                CareSummary = (req.CareSummary ?? "").Trim(),
                WateringNotes = (req.WateringNotes ?? "").Trim(),
                Source = req.Source,
                UpdatedAt = DateTime.UtcNow
            };
            s.Thresholds = req.Thresholds;
            return Results.Ok(await SaveAndSyncAsync(s, store, registry, orion, ct));
        });

        // ---- Leitura pelos outros serviços do EC2 ----

        // Para o dashboard React (sem e-mail nem variáveis privadas). CORS liberado só para as origens configuradas.
        app.MapGet("/api/public/projects/{projectId}/dashboard", async (string projectId, IAppStore store, CancellationToken ct) =>
        {
            var s = await store.GetSettingsAsync(projectId, ct);
            if (s is null) return NotFound();
            object? plant = s.Plant is null ? null : new
            {
                s.Plant.CommonName, s.Plant.ScientificName, s.Plant.Confidence,
                s.Plant.CareSummary, s.Plant.WateringNotes, s.Plant.Source, s.Plant.UpdatedAt
            };
            var vars = s.CustomVariables.Where(kv => kv.Value.ExposeToDashboard).ToDictionary(kv => kv.Key, kv => kv.Value.Value);
            return Results.Ok(new DashboardSettingsResponse(s.ProjectId, s.ReservoirAlertDistanceCm, s.Thresholds, plant, vars, s.UpdatedAt, s.Version));
        }).RequireCors("dashboard").WithTags("Leitura");

        // Para o Monitor .NET e a sua API: tudo (inclui e-mail e variáveis privadas). Exige X-Api-Key.
        app.MapGet("/api/internal/projects/{projectId}/settings", async (string projectId, HttpRequest http,
            IAppStore store, IOptions<SecurityOptions> sec, CancellationToken ct) =>
        {
            if (!PasswordHasher.SecretEquals(http.Headers["X-Api-Key"].ToString(), sec.Value.ServiceApiKey))
                return Results.Unauthorized();
            return await store.GetSettingsAsync(projectId, ct) is { } s ? Results.Ok(s) : NotFound();
        }).WithTags("Leitura");
    }

    private static IResult NotFound() => Problem(404, "Projeto não encontrado.");

    /// <summary>Retorna null se não existe ou se o usuário não é o dono (mesma resposta, para não vazar).</summary>
    private static async Task<ProjectSettings?> LoadOwnedAsync(string projectId, ClaimsPrincipal user, IAppStore store, CancellationToken ct)
    {
        var s = await store.GetSettingsAsync(projectId, ct);
        return s is not null && s.OwnerUserId == user.UserId() ? s : null;
    }

    private static async Task<SaveResult> SaveAndSyncAsync(ProjectSettings s, IAppStore store, ProjectRegistry registry,
        OrionSyncService orion, CancellationToken ct)
    {
        s.UpdatedAt = DateTime.UtcNow;
        s.Version++;
        await store.SaveSettingsAsync(s, ct);
        var def = registry.Find(s.ProjectId);
        var sync = def is null ? "projeto fora do appsettings" : await orion.PushAsync(def, s, ct);
        return new SaveResult(s, sync);
    }
}
