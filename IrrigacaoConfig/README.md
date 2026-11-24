# IrrigacaoConfig — variáveis globais + identificação de planta por foto

App ASP.NET Core (.NET 10) que roda no mesmo EC2 da irrigação inteligente:

- **Cadastro do dono**: a pessoa cria a conta e informa o ID do projeto (`irrig001`) e o **código de ativação**. A partir daí ela é a dona, e só ela edita o projeto.
- **Variáveis globais**: e-mail de alerta, limite do reservatório (cm), faixas de umidade, temperatura e luminosidade, e variáveis livres (chave → valor).
- **Planta por foto**: no celular, "Tirar foto" abre a câmera. O Gemini identifica a planta e sugere as faixas, o dono revisa/edita e clica em **Aplicar ao dashboard**. Também dá para digitar o nome da planta.
- **Para onde vão os dados**:
  - MongoDB (banco `irrigacao_config`): é a fonte da verdade.
  - Orion (opcional): atributos `cfg*` na mesma entidade da telemetria.
  - `GET /api/public/projects/{id}/dashboard`: para o React (sem e-mail nem variáveis privadas).
  - `GET /api/internal/projects/{id}/settings` com `X-Api-Key`: para o Monitor .NET (tudo).

```
Celular/navegador ──► IrrigacaoConfig :5080 ──► MongoDB (irrigacao_config)
        foto ─────────►      │  └──────────► Gemini (foto → planta + faixas)
                             ├──► Orion  POST /v2/entities/{id}/attrs  (cfgSoilMoistureMin…)
                             ├──► Dashboard React  GET /api/public/…/dashboard
                             └──► Monitor .NET     GET /api/internal/…/settings (X-Api-Key)
```

A IA continua só **orientando**: ela sugere faixas de alerta e cores, nada é salvo sem a confirmação do dono, e ela não aciona a bomba.

## Rodar localmente (sem Mongo)

```bash
cd IrrigacaoConfig
export Storage__Provider=Memory
export Projects__0__ClaimCode=TESTE123
export Gemini__ApiKey=SUA_CHAVE
dotnet run
# abra http://localhost:5080 → "Cadastrar projeto" → irrig001 / TESTE123
```

## Subir no EC2

1. Copie a pasta para o EC2 (ex.: ao lado do seu `docker-compose.yml`).
2. Junte o serviço de `examples/docker-compose.config.yml` ao seu compose e ajuste os nomes `mongo-db` e `orion` para os nomes dos serviços que você já usa.
3. Crie um `.env` (não vá para o Git):
   ```
   IRRIG001_CLAIM_CODE=um-codigo-dificil
   GEMINI_API_KEY=a-mesma-chave-que-sua-api-ja-usa
   CONFIG_SERVICE_API_KEY=gere-com-openssl-rand-hex-32
   ```
4. `docker compose up -d --build irrigacao-config`
5. Abra a **porta 5080** no Security Group (de preferência só para o seu IP).
6. Descubra o ID real da entidade no Orion e coloque em `Projects__0__OrionEntityId`:
   ```bash
   curl -s localhost:1026/v2/entities -H 'fiware-service: smart' -H 'fiware-servicepath: /'
   ```

> **HTTPS**: o login funciona em HTTP e o botão de câmera também (ele usa `<input capture>`, não `getUserMedia`). Mesmo assim, com senha trafegando, vale colocar um Nginx + Let's Encrypt na frente quando der.

## Ligar nos serviços que você já tem

| Serviço | O que muda | Arquivo |
|---|---|---|
| **Monitor .NET** | Em vez do e-mail e dos `300 cm` fixos, lê `AlertEmail` e `ReservoirAlertDistanceCm` a cada ciclo. Se o serviço cair, guarda o último valor. | `examples/MonitorSettingsClient.cs` |
| **Dashboard React** | Hook `useIrrigationSettings()` + `ReferenceArea` do Recharts para pintar a faixa ideal da planta. Também mostra o nome da planta. | `examples/useIrrigationSettings.tsx` |
| **Orion** (opcional) | Com `Orion__Enabled=true`, cada vez que salvar ele grava `cfgPlantName`, `cfgSoilMoistureMin/Max`, `cfgTemperatureMin/Max`, `cfgLuminosityMin/Max`, `cfgReservoirAlertDistanceCm` e as variáveis públicas como `cfg_<nome>`. Assim sua API ASP.NET pode ler tudo do mesmo lugar. | — |

Para o Orion: se a sua subscription do STH-Comet usa `"attrs": []` (todos os atributos), as mudanças de `cfg*` também vão gerar notificações para o histórico. Para evitar, liste na subscription só os atributos dos sensores.

## Configuração (`appsettings.json` ou variáveis de ambiente)

| Chave | Para quê |
|---|---|
| `Projects[n].ProjectId / ClaimCode` | Projetos que podem ser reivindicados. Funciona como a "senha de fábrica" do dispositivo. |
| `Projects[n].OrionEntityId / OrionEntityType` | A entidade criada pelo IoT Agent. |
| `Storage:ConnectionString` | O seu MongoDB. |
| `Gemini:ApiKey / Model` | O modelo padrão é `gemini-3.5-flash-lite` (o mesmo do seu chat). |
| `Orion:Enabled / BaseUrl / FiwareService / FiwareServicePath` | Sincronização com o Orion. |
| `Security:ServiceApiKey` | Chave do endpoint interno, usada pelo Monitor .NET. |
| `Security:DashboardOrigins` | Origens do React liberadas no CORS. |

Em variáveis de ambiente, `:` vira `__` (ex.: `Gemini__ApiKey`) e listas usam índice (`Projects__0__ClaimCode`).

## API

| Método | Rota | Quem chama |
|---|---|---|
| POST | `/api/auth/register` `{name,email,password,projectId,claimCode}` | tela de cadastro |
| POST | `/api/auth/login` · `/api/auth/logout` · GET `/api/auth/me` | tela |
| POST | `/api/projects/claim` `{projectId,claimCode}` | dono que adiciona outro projeto |
| GET/PUT | `/api/projects/{id}/settings` | dono |
| POST | `/api/projects/{id}/plant/identify` (multipart `photo`) | dono: foto → sugestão |
| POST | `/api/projects/{id}/plant/suggest` `{plantName}` | dono: nome → sugestão |
| POST | `/api/projects/{id}/plant/apply` | dono confirma a sugestão |
| GET | `/api/public/projects/{id}/dashboard` | React |
| GET | `/api/internal/projects/{id}/settings` + `X-Api-Key` | Monitor .NET / sua API |

## Segurança já incluída

- Senha com PBKDF2-SHA256 (210 mil iterações).
- Cookie HttpOnly e SameSite=Strict.
- Limite de tentativas: 10 por minuto no login/cadastro, por IP; 8 por minuto nas chamadas de IA, por usuário (segura o custo do Gemini).
- A chave do Gemini fica só no servidor.
- A foto é reduzida no celular para cerca de 1280 px antes do envio. Limite de 8 MB; aceita JPG, PNG, WEBP e HEIC.
- O e-mail nunca vai para o Orion nem para o endpoint público.
- Os valores da IA passam por limites físicos: um mínimo maior que o máximo é corrigido, e valores fora de 0–100 % são ajustados. Abaixo de 60 % de confiança, a tela pede para conferir.
