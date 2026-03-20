<!--
Sync Impact Report
- Version change: N/A → 1.0.0
- Added principles: Compliance-First, Core-UI Separation, Privacy-by-Design, Test-First, Observability, Simplicity
- Added sections: Security & Compliance Requirements, Development Workflow
- Templates requiring updates: ✅ plan-template.md (no changes needed), ✅ spec-template.md (no changes needed), ✅ tasks-template.md (no changes needed)
- Follow-up TODOs: None
-->

# BusinessProcessAgent Constitution

## Core Principles

### I. Compliance-First (NON-NEGOTIABLE)

All captured data MUST pass through the compliance pipeline before any external processing. The pipeline order is immutable: **Exclusion → Redaction → LLM → Encryption → Storage → Audit**. Redaction of PII and sensitive data MUST happen before LLM ingestion — no exceptions. If redaction fails, the data MUST be dropped (fail-closed). Every observation event MUST produce an audit log entry. Enterprise buyers MUST have controls to configure excluded applications, redaction patterns, encryption keys, and retention policies.

### II. Core-UI Separation

All business logic, services, and compliance enforcement MUST reside in the `BusinessProcessAgent.Core` library. The Core library MUST NOT reference any UI framework (WinUI 3, WPF, or WinForms). The WinUI 3 App project is a thin shell that wires services and renders UI. This separation ensures the Core is independently testable and could be reused with a different UI host.

### III. Privacy-by-Design

Screenshots and application context contain inherently sensitive data. Storage MUST be ephemeral or AES-256-GCM encrypted. Encryption keys MUST be protected via DPAPI or Azure Key Vault — never stored in plaintext. Users MUST be able to discard sessions and clear all data at any time. No data leaves the machine unencrypted. The application MUST respect system idle, lock, and screensaver states and pause observation automatically.

### IV. Test-First

New features MUST have tests written before implementation. The Core library is the primary test target — tests exercise services, compliance, and storage independently of the UI. xUnit is the test framework. Red-Green-Refactor cycle is enforced for all Core service changes.

### V. Observability

All services MUST use `ILogger<T>` for structured logging. The compliance audit log (`AuditLogger`) provides an append-only, tamper-evident record of all observation events. Errors MUST be logged with sufficient context for diagnosis. Silent failures are prohibited — every error path MUST either log or surface to the user.

### VI. Simplicity

Start with the simplest implementation that works. Avoid premature abstraction — add patterns (repositories, mediators, etc.) only when complexity is justified and documented. YAGNI applies: do not build features speculatively. The Win32 P/Invoke layer for tray icons and screen capture is necessarily complex but MUST be isolated behind clean service interfaces.

## Security & Compliance Requirements

- **Encryption**: AES-256-GCM for all persisted observation data. Master key protected by DPAPI (local machine) or environment variable / Azure Key Vault (enterprise).
- **Redaction**: Regex-based PII stripping (emails, phone numbers, SSNs, credit cards, IP addresses) plus configurable keyword blocklist. Screenshot regions containing detected PII MUST be pixelated before LLM analysis.
- **Exclusion**: Configurable application and window title exclusion lists. Excluded applications MUST NOT be captured at all — checked before screenshot is taken.
- **Retention**: Configurable retention period with automatic cleanup. Default is ephemeral (session-scoped).
- **Audit**: Append-only TSV audit log recording timestamp, event type, session ID, and anonymized metadata. Audit logs MUST NOT contain raw PII.
- **Secrets Management**: No secrets in source code. API keys, endpoints, and connection strings MUST come from `settings.json` (gitignored) or environment variables. The `.gitignore` MUST cover `.db`, `.enc`, `.env`, `settings.json`, `screenshots/`, and audit logs.

## Development Workflow

- **Solution format**: `.slnx` (new XML solution format for .NET 10).
- **Build**: `dotnet build BusinessProcessAgent.slnx` — MUST compile with zero errors. Warnings tracked but not blocking.
- **Test**: `dotnet test` — all tests MUST pass before merge.
- **Target**: `net10.0-windows10.0.19041.0` for all projects (Core, App, Tests).
- **UI framework**: WinUI 3 with Windows App SDK. No WPF or WinForms dependencies in the App project.
- **System tray**: Win32 `Shell_NotifyIcon` P/Invoke (WinUI 3 has no built-in tray support).
- **AI provider**: Azure OpenAI with `Azure.AI.OpenAI` SDK and `Azure.Identity` for authentication.
- **Storage**: SQLite via `Microsoft.Data.Sqlite` for process step and session persistence.
- **Commit convention**: Conventional commits (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`).
- **Code review**: All PRs MUST verify compliance pipeline is intact and no secrets are introduced.

## Governance

This constitution is the authoritative source of project principles and MUST be consulted before architectural decisions. All pull requests and code reviews MUST verify compliance with these principles. Deviations require explicit justification documented in the PR description with a reference to the principle being violated and the rationale.

Amendments to this constitution require:
1. A description of the proposed change and rationale.
2. An impact assessment on existing code and dependent artifacts.
3. Version increment following semantic versioning (MAJOR for principle removals/redefinitions, MINOR for additions, PATCH for clarifications).
4. Update of all dependent templates and documentation.

Runtime development guidance is available in `.github/copilot-instructions.md`.

**Version**: 1.0.0 | **Ratified**: 2026-03-17 | **Last Amended**: 2026-03-17
