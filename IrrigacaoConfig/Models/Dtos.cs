namespace IrrigacaoConfig.Models;

public record RegisterRequest(string Name, string Email, string Password, string ProjectId, string ClaimCode);
public record LoginRequest(string Email, string Password);
public record ClaimProjectRequest(string ProjectId, string ClaimCode);

public record MeResponse(string Id, string Name, string Email, IReadOnlyList<string> Projects);

public record SettingsUpdateRequest(
    string AlertEmail,
    double ReservoirAlertDistanceCm,
    Thresholds Thresholds,
    Dictionary<string, CustomVariable>? CustomVariables);

public record SaveResult(ProjectSettings Settings, string OrionSync);

/// <summary>Resposta estruturada que a IA devolve. Nada é salvo até o dono confirmar.</summary>
public class PlantSuggestion
{
    public bool IsPlant { get; set; }
    public string CommonName { get; set; } = "";
    public string ScientificName { get; set; } = "";
    public double Confidence { get; set; }
    public List<string> Alternatives { get; set; } = new();
    public string CareSummary { get; set; } = "";
    public string WateringNotes { get; set; } = "";
    public double SoilMoistureMin { get; set; }
    public double SoilMoistureMax { get; set; }
    public double TemperatureMin { get; set; }
    public double TemperatureMax { get; set; }
    public double LuminosityMin { get; set; }
    public double LuminosityMax { get; set; }
    public string LuminosityUnit { get; set; } = "%";
    /// <summary>Avisos gerados pelo servidor (confiança baixa, valores corrigidos...).</summary>
    public List<string> Warnings { get; set; } = new();
}

public record PlantByNameRequest(string PlantName);

/// <summary>O que o dono confirma (pode ter editado os valores sugeridos).</summary>
public record PlantApplyRequest(
    string CommonName,
    string? ScientificName,
    double Confidence,
    string? CareSummary,
    string? WateringNotes,
    string Source,
    Thresholds Thresholds);

/// <summary>Visão sem dados pessoais, para o dashboard React.</summary>
public record DashboardSettingsResponse(
    string ProjectId,
    double ReservoirAlertDistanceCm,
    Thresholds Thresholds,
    object? Plant,
    Dictionary<string, string> Variables,
    DateTime UpdatedAt,
    long Version);
