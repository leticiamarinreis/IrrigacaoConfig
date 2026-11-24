using MongoDB.Bson.Serialization.Attributes;

namespace IrrigacaoConfig.Models;

/// <summary>Usuário dono de um ou mais projetos de irrigação.</summary>
[BsonIgnoreExtraElements]
public class AppUser
{
    [BsonId] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>Sempre em minúsculas (normalizado).</summary>
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Variáveis globais de um projeto (ex.: irrig001). É este documento que o Monitor .NET,
/// o dashboard e o Orion consomem.
/// </summary>
[BsonIgnoreExtraElements]
public class ProjectSettings
{
    /// <summary>Mesmo id usado no tópico MQTT (/TEF/irrig001/attrs).</summary>
    [BsonId] public string ProjectId { get; set; } = "";
    public string OwnerUserId { get; set; } = "";

    /// <summary>E-mail que recebe os alertas do Monitor .NET.</summary>
    public string AlertEmail { get; set; } = "";

    /// <summary>Distância (cm) do sensor ultrassônico acima da qual o reservatório é considerado baixo.</summary>
    public double ReservoirAlertDistanceCm { get; set; } = 300;

    public Thresholds Thresholds { get; set; } = new();
    public PlantProfile? Plant { get; set; }

    /// <summary>Variáveis livres (chave → valor). Só as marcadas como públicas vão para o dashboard/Orion.</summary>
    public Dictionary<string, CustomVariable> CustomVariables { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;
}

/// <summary>Faixas-alvo que o dashboard usa para colorir gráficos e cartões.</summary>
public class Thresholds
{
    public double SoilMoistureMin { get; set; } = 30;   // %
    public double SoilMoistureMax { get; set; } = 70;   // %
    public double TemperatureMin { get; set; } = 15;    // °C
    public double TemperatureMax { get; set; } = 32;    // °C
    public double LuminosityMin { get; set; } = 30;
    public double LuminosityMax { get; set; } = 90;
    /// <summary>"%" ou "lux" — precisa bater com o que o ESP32 publica.</summary>
    public string LuminosityUnit { get; set; } = "%";
}

public class PlantProfile
{
    public string CommonName { get; set; } = "";
    public string ScientificName { get; set; } = "";
    /// <summary>0 a 1. Para cadastro manual fica 1.</summary>
    public double Confidence { get; set; }
    public string CareSummary { get; set; } = "";
    public string WateringNotes { get; set; } = "";
    /// <summary>"ia-foto", "ia-nome" ou "manual".</summary>
    public string Source { get; set; } = "manual";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CustomVariable
{
    public string Value { get; set; } = "";
    /// <summary>Se true, aparece no endpoint público do dashboard e é enviada ao Orion.</summary>
    public bool ExposeToDashboard { get; set; }
}
