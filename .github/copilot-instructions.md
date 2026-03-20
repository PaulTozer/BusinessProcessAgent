# Copilot Instructions — BusinessProcessAgent

## Build, Test & Run

```powershell
# Build entire solution
dotnet build BusinessProcessAgent.slnx

# Run xUnit tests
dotnet test tests/BusinessProcessAgent.Core.Tests

# Run a single test by name
dotnet test tests/BusinessProcessAgent.Core.Tests --filter "FullyQualifiedName~YourTestName"

# Run the WinUI 3 desktop app
dotnet run --project src/BusinessProcessAgent.App/BusinessProcessAgent.App.csproj

# Run the Foundry Agent (Python, from src/BusinessProcessAgent.Agent/)
pip install -r src/BusinessProcessAgent.Agent/requirements.txt
python src/BusinessProcessAgent.Agent/agent.py      # HTTP server
python src/BusinessProcessAgent.Agent/cli.py        # Interactive CLI
```

## Architecture

This is a Windows desktop agent that observes user activity and uses LLM vision to map business processes from screenshots and application context.

**Data pipeline** (every capture follows this exact flow — nothing bypasses compliance):
```
Foreground Poll → Exclusion Check → Screenshot Capture → Redact Text → Redact Screenshot → LLM Analysis → Encrypt → Store → Audit Log
```

**Core library** (`BusinessProcessAgent.Core`) — All business logic, no UI dependencies:
- `ForegroundWindowPoller` — Timer-driven Win32 polling (configurable interval, default 5s). Detects application context changes and raises `ContextChanged` events.
- `ScreenStateMonitor` — Detects screensaver/lock/suspend via `SystemEvents` and `SystemParametersInfo`; suppresses capture when user is idle.
- `ScreenCaptureService` — Captures foreground window via `PrintWindow`/`BitBlt`, resizes to ≤1280×720, returns base64 JPEG.
- `WindowContextResolver` — Static methods with `[GeneratedRegex]` that normalize window titles (Office suffixes, browser chrome, Teams, tab counts).
- `ProcessAnalysisService` — Sends redacted screenshot + context to Azure OpenAI vision model. Returns `LlmAnalysisResult` with high-level action, low-level action, user intent, and business process name.
- `ProcessAssembler` — Groups temporal sequences of `ProcessStep` records into `BusinessProcess` flows. Detects boundaries on process-name changes or 10-minute gaps.
- `ObservationCoordinator` — Orchestrator wiring poller → compliance → LLM → storage. Manages observation sessions, auto-captures on context change and at timed intervals.

**Compliance layer** (`BusinessProcessAgent.Core.Compliance`) — Enterprise data governance:
- `RedactionService` — Regex-based PII stripping (emails, SSNs, credit cards, IPs, phone numbers) plus configurable keyword blocklists. Pixelates screenshots before LLM ingestion.
- `EncryptionService` — AES-256-GCM encryption for data at rest. Key sources: DPAPI (user-scoped), environment variable, or Azure Key Vault.
- `AuditLogger` — Append-only TSV log recording every capture/redaction/LLM-call/storage event with metadata only (no sensitive content).
- `ComplianceSettings` — Admin-configurable controls: excluded apps, excluded title keywords, ephemeral mode, encryption, redaction patterns, data retention.

**Storage** (`BusinessProcessAgent.Core.Storage`):
- `ProcessStore` — PostgreSQL-backed persistence for observation sessions, process steps, and assembled business processes. Uses Npgsql. Thread-safe via `SemaphoreSlim`.

**Desktop app** (`BusinessProcessAgent.App`) — WinUI 3 with Windows App SDK:
- System tray icon via Win32 `Shell_NotifyIcon` (WinUI 3 has no built-in NotifyIcon).
- `TrayIconManager` — Bridges Core services to the UI thread. Left-click opens main window, right-click toggles observation.
- Three pages: `MainPage` (dashboard + live activity feed), `ProcessViewerPage` (two-pane process/step browser), `SettingsPage` (AI, observation, and compliance configuration).

**Foundry Agent** (`BusinessProcessAgent.Agent`) — Python, Microsoft Agent Framework:
- `agent.py` — Main entry point. Creates an `AzureAIClient`-based Foundry agent with deep business-analysis instructions. Runs as an HTTP server via `from_agent_framework`.
- `cli.py` — Interactive multi-turn CLI for local testing.
- `tools.py` — Read-only PostgreSQL tools that query the same database the desktop app writes to: `list_business_processes`, `get_process_steps`, `get_recent_activity`, `get_session_summary`, `list_sessions`, `get_application_usage_stats`, `find_process_bottlenecks`.

## Key Conventions

**Target framework**: .NET 10.0. Core and tests target `net10.0-windows10.0.19041.0`. App uses WinUI 3 via Windows App SDK (`UseWinUI`).

**Models are records**: All domain models (`ProcessStep`, `ApplicationContext`, `ScreenCapture`, `BusinessProcess`, `ObservationSession`, `LlmAnalysisResult`) are C# `record` types with `required` and `init` properties.

**Compliance-first pipeline**: The `ObservationCoordinator` enforces that all data flows through exclusion checks → redaction → LLM → encryption → storage → audit. No shortcut path exists. When adding new data flows, always route through this pipeline.

**Fail closed**: If redaction fails, the screenshot is dropped (not sent to LLM). If encryption fails, data is not stored. Never degrade toward less security.

**Win32 interop**: All P/Invoke declarations live in `NativeMethods.cs` (Core) or `TrayIcon.cs`/`TrayMessageWindow.cs` (App). Do not scatter `DllImport` across other files.

**Thread safety**: Each service uses its own synchronization mechanism:
- `ProcessStore` → `SemaphoreSlim`
- `ProcessAnalysisService` → `lock` for client reconfiguration
- `ForegroundWindowPoller` → `lock` on context state

**Configuration**: `AppSettings` with nested sealed classes (`AzureAiSettings`, `ObservationSettings`, `ComplianceSettings`). JSON file at `data/settings.json`, loaded/saved via static methods.

**UI threading**: Background callbacks marshal to the WinUI `DispatcherQueue` for UI updates. The Core library never references UI types.

**Tests**: xUnit with `[Fact]` and `[Theory]`. Test method names describe the scenario.

**Foundry Agent (Python)**: Uses `AzureAIClient` (not legacy `AzureAIAgentClient`). Async credentials from `azure.identity.aio`. SDK pinned to `agent-framework-*==1.0.0rc3` and `azure-ai-agentserver-*==1.0.0b16`. Entry point uses `from_agent_framework(agent).run_async()` for HTTP server mode. Deployed as an Azure Container App. Environment variables loaded via `load_dotenv(override=False)` so Foundry runtime env vars take precedence. Agent tools are read-only queries against the shared Azure PostgreSQL database via `asyncpg`.
