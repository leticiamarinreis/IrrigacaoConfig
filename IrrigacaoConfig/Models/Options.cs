namespace IrrigacaoConfig.Models;

/// <summary>Projeto que pode ser reivindicado. O ClaimCode funciona como "senha de fábrica" do dispositivo.</summary>
public class ProjectDefinition
{
    public string ProjectId { get; set; } = "";
    public string ClaimCode { get; set; } = "";
    /// <summary>Entidade do Orion criada pelo IoT Agent (ex.: urn:ngsi-ld:Irrigation:001).</summary>
    public string OrionEntityId { get; set; } = "";
    public string OrionEntityType { get; set; } = "";
}

public class StorageOptions
{
    /// <summary>"Mongo" (produção) ou "Memory" (só para testes locais).</summary>
    public string Provider { get; set; } = "Mongo";
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string Database { get; set; } = "irrigacao_config";
}

public class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public int TimeoutSeconds { get; set; } = 45;
}

public class OrionOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:1026";
    public string FiwareService { get; set; } = "smart";
    public string FiwareServicePath { get; set; } = "/";
    /// <summary>Prefixo dos atributos de configuração, para não colidir com os da telemetria.</summary>
    public string AttributePrefix { get; set; } = "cfg";
}

public class SecurityOptions
{
    /// <summary>Chave que o Monitor .NET e a sua API enviam em X-Api-Key para ler as configurações completas.</summary>
    public string ServiceApiKey { get; set; } = "";
    public string[] DashboardOrigins { get; set; } = [];
}
