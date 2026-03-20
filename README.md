# BusinessProcessAgent

A Windows desktop agent that observes user activity and uses LLM vision to automatically map business processes from screenshots and application context.

The agent runs quietly in the system tray, captures the foreground window at configurable intervals, redacts sensitive data, sends the screenshot to an Azure OpenAI vision model for analysis, and assembles the results into structured business process flows — all with a compliance-first pipeline that ensures PII never leaves the device unprotected.

## Prerequisites

- Windows 10 (build 19041) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Python 3.10+](https://www.python.org/downloads/) (for the Foundry Agent)
- An Azure AI Foundry project with a model deployment (e.g. `gpt-4o`)
- An [Azure Database for PostgreSQL Flexible Server](https://learn.microsoft.com/azure/postgresql/flexible-server/overview)
- (For deployment) An [Azure Container App](https://learn.microsoft.com/azure/container-apps/overview) environment + [Azure Container Registry](https://learn.microsoft.com/azure/container-registry/)

## Quick Start

```powershell
# Clone and build
git clone <repo-url>
cd BusinessProcessAgent
dotnet build BusinessProcessAgent.slnx

# Run the desktop app
dotnet run --project src/BusinessProcessAgent.App/BusinessProcessAgent.App.csproj
```

On first launch the app appears in the system tray. Right-click the tray icon to start an observation session. Open the main window (left-click) to configure your Azure OpenAI endpoint in **Settings**.

### Run the Workflow Analyst Agent

```powershell
cd src/BusinessProcessAgent.Agent
pip install -r requirements.txt

# Copy and fill in your Azure AI Foundry credentials
copy .env.template .env
# Edit .env with your FOUNDRY_PROJECT_ENDPOINT, FOUNDRY_MODEL_DEPLOYMENT_NAME, and BPA_DATABASE_URL

# Interactive CLI mode
python cli.py

# HTTP server mode (for Foundry deployment / Agent Inspector)
python agent.py
```

The desktop app and the agent both connect to the same Azure PostgreSQL database, so multiple users can interact with the agent while someone else captures workflows.

## Configuration

Settings are stored in `data/settings.json` and can be edited via the Settings page or by modifying the file directly.

### Azure AI

| Setting | Default | Description |
|---------|---------|-------------|
| `azureAi.endpoint` | *(empty)* | Azure OpenAI resource endpoint URL |
| `azureAi.apiKey` | *(empty)* | API key for the endpoint |
| `azureAi.model` | `gpt-4o-mini` | Deployed model name |
| `azureAi.enabled` | `false` | Master switch for LLM analysis |

### Database

| Setting | Default | Description |
|---------|---------|-------------|
| `database.connectionString` | *(empty)* | PostgreSQL connection string (e.g. `Host=myserver.postgres.database.azure.com;Port=5432;Database=bpa;Username=bpa_user;Password=***;Ssl Mode=Require;`) |

### Observation

| Setting | Default | Description |
|---------|---------|-------------|
| `observation.pollingIntervalSeconds` | `5` | How often the foreground window is checked |
| `observation.captureIntervalSeconds` | `15` | Interval between automatic screenshot captures |
| `observation.captureOnContextChange` | `true` | Also capture when the active application changes |
| `observation.screenshotDirectory` | `data/screenshots` | Where screenshot files are stored |

### Compliance

| Setting | Default | Description |
|---------|---------|-------------|
| `compliance.captureEnabled` | `true` | Master capture switch |
| `compliance.redactScreenshots` | `true` | Blur text regions matching redaction rules before LLM ingestion |
| `compliance.redactionPatterns` | *(see below)* | Regex patterns for PII stripping |
| `compliance.redactionKeywords` | `[]` | Exact keywords always replaced with `[REDACTED]` |
| `compliance.excludedApplications` | `1Password, KeePass, ...` | Process names that are never captured |
| `compliance.excludedTitleKeywords` | `password, credential, ...` | Window title substrings that suppress capture |
| `compliance.ephemeralScreenshots` | `false` | Keep screenshots in memory only (never written to disk) |
| `compliance.encryptAtRest` | `true` | AES-256-GCM encryption for stored data |
| `compliance.encryptionKeySource` | `DPAPI` | Key source: `DPAPI`, `Environment`, or `KeyVault` |
| `compliance.keyVaultUri` | *(empty)* | Azure Key Vault URI (when using `KeyVault` key source) |
| `compliance.keyVaultKeyName` | *(empty)* | Key name inside Key Vault |
| `compliance.dataRetentionDays` | `30` | Auto-delete data older than N days (0 = disabled) |
| `compliance.auditLoggingEnabled` | `true` | Write every event to the append-only audit log |

**Default redaction patterns** match: email addresses, UK National Insurance numbers, US Social Security numbers, credit card numbers, international phone numbers, and IP addresses.

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                   WinUI 3 Desktop App                    │
│  MainPage · ProcessViewerPage · SettingsPage · TrayIcon  │
└────────────────────────┬─────────────────────────────────┘
                         │ events / commands
┌────────────────────────▼─────────────────────────────────┐
│                   Core Library                           │
│                                                          │
│  ObservationCoordinator (orchestrator)                   │
│    ├─ ForegroundWindowPoller  — Win32 polling             │
│    ├─ ScreenStateMonitor      — lock / screensaver       │
│    ├─ ScreenCaptureService    — PrintWindow / BitBlt     │
│    ├─ WindowContextResolver   — title normalization      │
│    ├─ RedactionService        — PII scrub (text + image) │
│    ├─ ProcessAnalysisService  — Azure OpenAI vision call │
│    ├─ ProcessAssembler        — group steps → processes  │
│    ├─ EncryptionService       — AES-256-GCM at rest      │
│    ├─ ProcessStore            — PostgreSQL persistence   │
│    └─ AuditLogger             — append-only TSV log      │
└──────────────────────────────────────────────────────────┘
                         │
               ┌─────────▼──────────┐
               │  Azure PostgreSQL  │  ← shared database
               └─────────┬──────────┘
                         │
┌────────────────────────▼─────────────────────────────────┐
│              Foundry Analyst Agent                        │
│         (Azure Container App / local)                    │
│  agent.py · cli.py · tools.py                            │
└──────────────────────────────────────────────────────────┘
```

### Data Pipeline

Every capture follows this exact flow — nothing bypasses compliance:

```
Foreground Poll → Exclusion Check → Screenshot Capture → Redact Text
    → Redact Screenshot → LLM Analysis → Encrypt → Store → Audit Log
```

- **Fail closed**: If redaction fails, the screenshot is dropped. If encryption fails, data is not stored.
- **Thread safety**: Each service uses its own synchronization (`SemaphoreSlim`, `lock`).

### Projects

| Project | Description |
|---------|-------------|
| `BusinessProcessAgent.Core` | All business logic. No UI dependencies. Targets `net10.0-windows10.0.19041.0`. |
| `BusinessProcessAgent.App` | WinUI 3 desktop app with system tray, three pages, and Windows App SDK. |
| `BusinessProcessAgent.Agent` | Python Foundry Agent — workflow analysis, Q&A, and improvement suggestions. Deployed as an Azure Container App. |
| `BusinessProcessAgent.Core.Tests` | xUnit test project for the Core library. |

### Domain Models

All models are C# `record` types with `required` / `init` properties:

- **`ProcessStep`** — A single observed action: application name, window title, high/low-level actions, user intent, business process name, confidence score, and optional screenshot path.
- **`BusinessProcess`** — An assembled sequence of steps with a name, description, observation count, and first/last seen timestamps.
- **`ApplicationContext`** — Snapshot of the current foreground window (process name, title, executable path).
- **`ScreenCapture`** — Base64 JPEG of the captured window (resized to ≤ 1280×720).
- **`ObservationSession`** — A time-bounded recording session with start/end timestamps.
- **`LlmAnalysisResult`** — Raw structured output from the LLM: high-level action, low-level action, user intent, business process name, and confidence.

## Testing

```powershell
# Run all tests
dotnet test tests/BusinessProcessAgent.Core.Tests

# Run a single test
dotnet test tests/BusinessProcessAgent.Core.Tests --filter "FullyQualifiedName~YourTestName"
```

Tests use xUnit with `[Fact]` and `[Theory]`. Test method names describe the scenario being verified.

## Privacy & Security

This application captures screenshots of user activity. It is designed with enterprise data governance in mind:

- **PII redaction** — Text and image content are scrubbed against configurable patterns and keyword lists before reaching the LLM or storage.
- **Encryption at rest** — All persisted data can be encrypted with AES-256-GCM. Key material is sourced from DPAPI (default), an environment variable, or Azure Key Vault.
- **Excluded applications** — Password managers, private browsing windows, and other sensitive apps are excluded by default.
- **Ephemeral mode** — Screenshots can be kept in memory only and never written to disk.
- **Audit logging** — Every capture, redaction, and LLM call is recorded in an append-only log with metadata only (no sensitive content).
- **Data retention** — Stored data is automatically purged after a configurable number of days.

## Workflow Analyst Agent

The `BusinessProcessAgent.Agent` is a Microsoft Foundry Agent built with the [Microsoft Agent Framework](https://learn.microsoft.com/azure/ai-services/agents/) that analyses the workflows captured by the desktop app.

### Capabilities

- **Process discovery** — Lists all captured business processes with observation counts and date ranges.
- **Step-by-step analysis** — Retrieves and reasons about the exact sequence of actions in any process.
- **Bottleneck detection** — Identifies application switches, timing gaps, low-confidence steps, and repeated actions.
- **Application usage stats** — Shows which apps are used most and across which processes.
- **Improvement suggestions** — Recommends concrete changes grounded in lean methodology, automation (RPA / Power Automate), process redesign, and tooling consolidation.
- **Business outcome reasoning** — Connects every recommendation to measurable impact: time saved, error reduction, compliance risk, employee experience, and cost.
- **Multi-turn conversation** — Maintains session context so users can drill into specifics.

### Agent Tools

| Tool | Description |
|------|-------------|
| `list_business_processes` | Overview of all discovered workflows |
| `get_process_steps` | Detailed step breakdown for a specific process |
| `get_recent_activity` | Latest observed actions across all sessions |
| `get_session_summary` | Full summary of a single observation session |
| `list_sessions` | Browse recent observation sessions |
| `get_application_usage_stats` | Per-application usage statistics |
| `find_process_bottlenecks` | Timing gaps, app switches, and low-confidence steps |

### Debugging

VS Code launch configurations are included for both HTTP server and CLI modes:

- **Debug Agent HTTP Server** — Launches with `agentdev` + `debugpy`, then opens the AI Toolkit Agent Inspector.
- **Debug Agent CLI** — Attaches the debugger to the interactive CLI session.

Install the debug tooling:

```powershell
pip install debugpy agent-dev-cli --pre
```

Then use the **Run and Debug** panel in VS Code to select a configuration.

### Deploying to Azure Container Apps

Build and push the container image, then create the Container App:

```bash
# Build the container image
az acr build --registry <your-acr> --image bpa-agent:latest src/BusinessProcessAgent.Agent/

# Create the Container App
az containerapp create \
  --name bpa-agent \
  --resource-group <your-rg> \
  --environment <your-container-env> \
  --image <your-acr>.azurecr.io/bpa-agent:latest \
  --target-port 8080 \
  --ingress external \
  --env-vars \
    FOUNDRY_PROJECT_ENDPOINT=<endpoint> \
    FOUNDRY_MODEL_DEPLOYMENT_NAME=<model> \
    BPA_DATABASE_URL=<postgresql-connection-url>
```

The agent runs as an HTTP server inside the container and is accessible to any authorised user.

## License

*No license specified yet.*
