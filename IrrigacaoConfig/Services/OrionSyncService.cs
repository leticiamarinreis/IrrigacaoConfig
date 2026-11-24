using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using IrrigacaoConfig.Models;
using Microsoft.Extensions.Options;

namespace IrrigacaoConfig.Services;

/// <summary>
/// Publica as configurações como atributos (cfg*) na mesma entidade do Orion que o IoT Agent atualiza,
/// via NGSI-v2 POST /v2/entities/{id}/attrs (cria ou atualiza atributos).
/// O e-mail NÃO vai para o Orion: o Monitor .NET lê pelo endpoint interno.
/// </summary>
public sealed class OrionSyncService(HttpClient http, IOptions<OrionOptions> options, ILogger<OrionSyncService> logger)
{
    // Caracteres proibidos pelo Orion em valores de texto (NGSI-v2).
    private static readonly char[] Forbidden = ['<', '>', '"', '\'', '=', ';', '(', ')'];

    public async Task<string> PushAsync(ProjectDefinition project, ProjectSettings s, CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Enabled) return "desativado";
        if (string.IsNullOrWhiteSpace(project.OrionEntityId)) return "sem OrionEntityId configurado";

        var p = opt.AttributePrefix;
        var t = s.Thresholds;
        var attrs = new JsonObject
        {
            [$"{p}PlantName"] = Text(s.Plant?.CommonName ?? ""),
            [$"{p}PlantScientificName"] = Text(s.Plant?.ScientificName ?? ""),
            [$"{p}SoilMoistureMin"] = Num(t.SoilMoistureMin),
            [$"{p}SoilMoistureMax"] = Num(t.SoilMoistureMax),
            [$"{p}TemperatureMin"] = Num(t.TemperatureMin),
            [$"{p}TemperatureMax"] = Num(t.TemperatureMax),
            [$"{p}LuminosityMin"] = Num(t.LuminosityMin),
            [$"{p}LuminosityMax"] = Num(t.LuminosityMax),
            [$"{p}LuminosityUnit"] = Text(t.LuminosityUnit == "%" ? "percent" : "lux"),
            [$"{p}ReservoirAlertDistanceCm"] = Num(s.ReservoirAlertDistanceCm),
            [$"{p}Version"] = Num(s.Version),
            [$"{p}UpdatedAt"] = new JsonObject { ["type"] = "DateTime", ["value"] = s.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) }
        };
        foreach (var (key, v) in s.CustomVariables.Where(kv => kv.Value.ExposeToDashboard))
            attrs[$"{p}_{key}"] = double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? Num(n) : Text(v.Value);

        // IDs NGSI-v2 não podem ter espaço, / ? # &: vão crus no path (o Orion não decodifica %3A).
        var url = $"{opt.BaseUrl.TrimEnd('/')}/v2/entities/{project.OrionEntityId}/attrs";
        if (!string.IsNullOrWhiteSpace(project.OrionEntityType)) url += $"?type={Uri.EscapeDataString(project.OrionEntityType)}";

        var content = new StringContent(attrs.ToJsonString(), Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"); // sem ";charset"
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.Add("fiware-service", opt.FiwareService);
        req.Headers.Add("fiware-servicepath", opt.FiwareServicePath);

        try
        {
            using var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return "ok";
            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Orion respondeu {Status}: {Body}", (int)resp.StatusCode, body);
            return $"falhou ({(int)resp.StatusCode})";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Orion inacessível");
            return "falhou (Orion inacessível)";
        }
    }

    private static JsonObject Num(double v) => new() { ["type"] = "Number", ["value"] = v };
    private static JsonObject Text(string v) => new() { ["type"] = "Text", ["value"] = Sanitize(v) };

    public static string Sanitize(string v)
    {
        var sb = new StringBuilder(v.Length);
        foreach (var c in v)
            if (!char.IsControl(c) && Array.IndexOf(Forbidden, c) < 0) sb.Append(c);
        var r = sb.ToString().Trim();
        return r.Length > 256 ? r[..256] : r;
    }
}

/// <summary>Projetos que existem no seu EC2 (lidos do appsettings / variáveis de ambiente).</summary>
public sealed class ProjectRegistry(IConfiguration config)
{
    private readonly Dictionary<string, ProjectDefinition> _projects =
        (config.GetSection("Projects").Get<List<ProjectDefinition>>() ?? [])
        .Where(p => !string.IsNullOrWhiteSpace(p.ProjectId))
        .ToDictionary(p => p.ProjectId, StringComparer.Ordinal);

    public ProjectDefinition? Find(string projectId) => _projects.GetValueOrDefault(projectId);
}
