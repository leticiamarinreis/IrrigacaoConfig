// Cole no dashboard React + TypeScript.
// Lê as faixas da planta e desenha no Recharts. Endpoint público (sem e-mail nem variáveis privadas).
// Lembre de colocar a origem do dashboard em Security:DashboardOrigins no app de configuração.

import { useEffect, useState } from "react";
import { ReferenceArea } from "recharts";

const CONFIG_URL = import.meta.env.VITE_CONFIG_URL ?? "http://SEU-IP-EC2:5080";

export interface Thresholds {
  soilMoistureMin: number; soilMoistureMax: number;
  temperatureMin: number; temperatureMax: number;
  luminosityMin: number; luminosityMax: number;
  luminosityUnit: "%" | "lux";
}

export interface DashboardSettings {
  projectId: string;
  reservoirAlertDistanceCm: number;
  thresholds: Thresholds;
  plant: { commonName: string; scientificName: string; confidence: number;
           careSummary: string; wateringNotes: string; source: string } | null;
  variables: Record<string, string>;
  updatedAt: string;
  version: number;
}

/** Busca e atualiza a cada `refreshMs` (padrão 60 s): mudanças no app aparecem sem recarregar. */
export function useIrrigationSettings(projectId = "irrig001", refreshMs = 60_000) {
  const [settings, setSettings] = useState<DashboardSettings | null>(null);
  useEffect(() => {
    let alive = true;
    const load = () =>
      fetch(`${CONFIG_URL}/api/public/projects/${projectId}/dashboard`)
        .then((r) => (r.ok ? r.json() : null))
        .then((d) => { if (alive && d) setSettings(d); })
        .catch(() => {});
    load();
    const id = setInterval(load, refreshMs);
    return () => { alive = false; clearInterval(id); };
  }, [projectId, refreshMs]);
  return settings;
}

/** "ok" | "baixo" | "alto" para colorir os cartões de métricas. */
export function statusOf(value: number, min: number, max: number) {
  return value < min ? "baixo" : value > max ? "alto" : "ok";
}

/*
Uso no gráfico de umidade do solo (dentro de <LineChart>):

  const cfg = useIrrigationSettings();
  ...
  {cfg && (
    <ReferenceArea
      y1={cfg.thresholds.soilMoistureMin}
      y2={cfg.thresholds.soilMoistureMax}
      fill="#2f7d32" fillOpacity={0.08}
      label={`Ideal p/ ${cfg.plant?.commonName ?? "planta"}`}
    />
  )}

E no cartão do reservatório:
  const baixo = distancia > (cfg?.reservoirAlertDistanceCm ?? 300);
*/
export { ReferenceArea };
