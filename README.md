# BusinessProcessAgent

A Windows desktop agent that observes user activity and uses LLM vision to automatically map business processes from screenshots and application context.

The agent runs quietly in the system tray, captures the foreground window at configurable intervals, redacts sensitive data, sends the screenshot to an Azure OpenAI vision model for analysis, and assembles the results into structured business process flows — all with a compliance-first pipeline that ensures PII never leaves the device unprotected.

## Prerequisites

- Windows 10 (build 19041) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An Azure OpenAI resource with a vision-capable model deployed (e.g. `gpt-4o-mini`)

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

## Configuration

Settings are stored in `data/settings.json` and can be edited via the Settings page or by modifying the file directly.

### Azure AI

| Setting | Default | Description |
|---------|---------|-------------|
| `azureAi.endpoint` | *(empty)* | Azure OpenAI resource endpoint URL |
| `azureAi.apiKey` | *(empty)* | API key for the endpoint |
| `azureAi.model` | `gpt-4o-mini` | Deployed model name |
| `azureAi.enabled` | `false` | Master switch for LLM analysis |

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
│    ├─ ProcessStore            — SQLite persistence       │
│    └─ AuditLogger             — append-only TSV log      │
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

## License

*No license specified yet.*
