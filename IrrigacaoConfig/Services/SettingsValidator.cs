using System.Net.Mail;
using System.Text.RegularExpressions;
using IrrigacaoConfig.Models;

namespace IrrigacaoConfig.Services;

public static partial class SettingsValidator
{
    public const int MaxCustomVariables = 30;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,39}$")]
    private static partial Regex VariableKeyRegex();

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        return MailAddress.TryCreate(email.Trim(), out var addr) && addr.Address == email.Trim() && email.Contains('.');
    }

    public static Dictionary<string, string[]> Validate(SettingsUpdateRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string key, string msg) => errors[key] = errors.TryGetValue(key, out var e) ? [.. e, msg] : [msg];

        if (!IsValidEmail(req.AlertEmail)) Add("alertEmail", "Informe um e-mail válido para os alertas.");
        if (req.ReservoirAlertDistanceCm is <= 0 or > 1000) Add("reservoirAlertDistanceCm", "Use um valor entre 1 e 1000 cm.");

        ValidateThresholds(req.Thresholds, Add);

        if (req.CustomVariables is { } vars)
        {
            if (vars.Count > MaxCustomVariables) Add("customVariables", $"Máximo de {MaxCustomVariables} variáveis.");
            foreach (var (key, v) in vars)
            {
                if (!VariableKeyRegex().IsMatch(key))
                    Add("customVariables", $"Nome inválido: '{key}'. Use letras, números e _ (começando por letra, até 40).");
                if (v is null || v.Value is null || v.Value.Length > 500)
                    Add("customVariables", $"Valor de '{key}' vazio ou maior que 500 caracteres.");
            }
        }
        return errors;
    }

    public static void ValidateThresholds(Thresholds? t, Action<string, string> add)
    {
        if (t is null) { add("thresholds", "Faixas obrigatórias."); return; }
        if (t.SoilMoistureMin < 0 || t.SoilMoistureMax > 100 || t.SoilMoistureMin >= t.SoilMoistureMax)
            add("thresholds.soilMoisture", "Umidade do solo: 0 ≤ mínimo < máximo ≤ 100.");
        if (t.TemperatureMin < -20 || t.TemperatureMax > 70 || t.TemperatureMin >= t.TemperatureMax)
            add("thresholds.temperature", "Temperatura: -20 ≤ mínimo < máximo ≤ 70 °C.");
        if (t.LuminosityUnit is not ("%" or "lux"))
            add("thresholds.luminosityUnit", "Unidade de luminosidade deve ser '%' ou 'lux'.");
        var lumMax = t.LuminosityUnit == "lux" ? 200_000 : 100;
        if (t.LuminosityMin < 0 || t.LuminosityMax > lumMax || t.LuminosityMin >= t.LuminosityMax)
            add("thresholds.luminosity", $"Luminosidade: 0 ≤ mínimo < máximo ≤ {lumMax}.");
    }
}
