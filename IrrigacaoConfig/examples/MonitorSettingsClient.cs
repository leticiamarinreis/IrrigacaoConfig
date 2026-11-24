// Cole no projeto do Monitor .NET.
// Substitui o e-mail e o limite de 300 cm fixos no código pelas variáveis globais do app.
//
// Program.cs do Monitor:
//   builder.Services.AddHttpClient<MonitorSettingsClient>(c =>
//   {
//       c.BaseAddress = new Uri("http://irrigacao-config:5080"); // ou http://localhost:5080
//       c.DefaultRequestHeaders.Add("X-Api-Key", builder.Configuration["ConfigService:ApiKey"]);
//   });
//
// No loop de 5 minutos:
//   var cfg = await settingsClient.GetAsync("irrig001", ct);
//   if (distanciaAtual > cfg.ReservoirAlertDistanceCm) await EnviarEmail(cfg.AlertEmail, ...);
//   if (umidadeAtual < cfg.Thresholds.SoilMoistureMin) ... // alerta extra opcional

using System.Net.Http.Json;

public sealed class MonitorSettingsClient(HttpClient http, ILogger<MonitorSettingsClient> logger)
{
    private MonitorSettings? _last; // último valor bom: se o serviço cair, o Monitor continua alertando

    public async Task<MonitorSettings> GetAsync(string projectId, CancellationToken ct)
    {
        try
        {
            _last = await http.GetFromJsonAsync<MonitorSettings>($"/api/internal/projects/{projectId}/settings", ct)
                    ?? throw new InvalidOperationException("Resposta vazia");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Não consegui ler as configurações; usando o último valor conhecido.");
        }
        return _last ?? MonitorSettings.Fallback;
    }
}

public record MonitorThresholds(
    double SoilMoistureMin, double SoilMoistureMax,
    double TemperatureMin, double TemperatureMax,
    double LuminosityMin, double LuminosityMax, string LuminosityUnit);

public record MonitorPlant(string CommonName, string ScientificName);

public record MonitorCustomVariable(string Value, bool ExposeToDashboard);

public record MonitorSettings(
    string ProjectId,
    string AlertEmail,
    double ReservoirAlertDistanceCm,
    MonitorThresholds Thresholds,
    MonitorPlant? Plant,
    Dictionary<string, MonitorCustomVariable> CustomVariables)
{
    // Valores atuais do seu sistema, usados até a primeira leitura dar certo.
    public static readonly MonitorSettings Fallback = new(
        "irrig001", "seu-email@exemplo.com", 300,
        new MonitorThresholds(30, 70, 15, 32, 30, 90, "%"), null, new());
}
