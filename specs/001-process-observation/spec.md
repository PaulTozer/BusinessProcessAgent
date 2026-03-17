# Feature Specification: Business Process Observation and Mapping

**Feature Branch**: `001-process-observation`
**Created**: 2026-03-17
**Status**: Draft
**Input**: User description: "Business process observation and mapping using screenshots, LLM vision, and temporal ordering"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Observe and Record User Activity (Priority: P1)

A knowledge worker launches the BusinessProcessAgent and clicks "Start Observing." As they go about their normal work — switching between applications, filling in forms, navigating menus — the agent silently captures screenshots at regular intervals and whenever the active application changes. The worker sees a live activity feed showing what the agent is observing. When done, they click "Stop Observing" to end the session. All captured data stays on their local machine and sensitive information is redacted before any analysis occurs.

**Why this priority**: This is the foundational capability — without observation and capture, no other feature can function. It delivers immediate value by creating a timestamped record of work activity.

**Independent Test**: Can be fully tested by starting observation, switching between 3-4 applications, and stopping. The activity feed should show captured entries with timestamps, application names, and window titles. Screenshots should be stored locally.

**Acceptance Scenarios**:

1. **Given** the agent is running in the system tray, **When** the user clicks "Start Observing," **Then** the agent begins capturing screenshots at the configured interval and the activity feed shows live entries.
2. **Given** observation is active, **When** the user switches from one application to another, **Then** the agent detects the context change and immediately captures a screenshot of the new foreground window.
3. **Given** observation is active, **When** the user's machine is locked or idle, **Then** the agent pauses capture automatically and resumes when the user returns.
4. **Given** observation is active and the user has configured excluded applications, **When** an excluded application is in the foreground, **Then** no screenshot is captured and no data is recorded for that application.
5. **Given** observation is active, **When** the user clicks "Stop Observing," **Then** capture stops, all pending data is saved, and the session is finalized.

---

### User Story 2 - Analyze Actions with LLM Vision (Priority: P2)

After a screenshot is captured, the agent sends it (with sensitive information already redacted) to an LLM vision model for analysis. The LLM identifies what the user was doing: the high-level business process (e.g., "Processing an expense report"), the specific low-level step (e.g., "Entering receipt amount in the Amount field"), and the user's likely intent. The agent accumulates these analyzed steps and groups them into coherent business processes based on process name similarity and temporal proximity.

**Why this priority**: Analysis transforms raw screenshots into meaningful business process data. Without this, the captured data is just a collection of images with no actionable insight.

**Independent Test**: Can be tested by providing a set of pre-captured screenshots to the analysis service and verifying that each returns a structured result with process name, high-level description, low-level step, user intent, and confidence score.

**Acceptance Scenarios**:

1. **Given** a screenshot has been captured and redacted, **When** the agent sends it for analysis, **Then** the LLM returns a structured result identifying the business process name, high-level action, low-level step detail, and user intent.
2. **Given** the LLM returns analysis results for multiple sequential screenshots, **When** the results share a common process name and occur within a configurable time window, **Then** the agent groups them into a single business process with ordered steps.
3. **Given** the LLM analysis fails or returns low confidence, **When** the result confidence is below the configured threshold, **Then** the step is still recorded but flagged as low-confidence for user review.
4. **Given** sensitive data is present in the screenshot, **When** the screenshot is sent for analysis, **Then** PII (emails, phone numbers, financial data) has already been redacted or pixelated before the LLM receives it.

---

### User Story 3 - Browse and Review Discovered Processes (Priority: P3)

After one or more observation sessions, the user opens the process viewer to browse all discovered business processes. They see a list of processes on the left (e.g., "Expense Report Submission," "Customer Onboarding") and can click any process to see its ordered steps on the right. Each step shows the timestamp, application context, high-level and low-level descriptions, and the user's inferred intent. The user can review, validate, and understand the processes the agent has identified.

**Why this priority**: Viewing and reviewing is how users get value from the captured data. It closes the feedback loop — the user can see what the agent learned and verify accuracy.

**Independent Test**: Can be tested by pre-populating the data store with sample processes and steps, then opening the process viewer and verifying the two-pane layout shows processes on the left and steps on the right with all expected detail fields.

**Acceptance Scenarios**:

1. **Given** observation sessions have produced analyzed steps, **When** the user opens the process viewer, **Then** they see a list of all discovered business processes with names and step counts.
2. **Given** the process list is displayed, **When** the user selects a process, **Then** the right pane shows all steps in chronological order with timestamp, application, high-level description, low-level detail, and intent.
3. **Given** the user has completed multiple sessions, **When** the same business process was performed across different sessions, **Then** steps from all sessions are consolidated under a single process entry.

---

### User Story 4 - Discard and Restart Observation (Priority: P4)

During an observation session, the user realizes they made a significant mistake in the process they were performing and wants to start over. They click "Discard & Restart" to delete the current session's data and begin a fresh observation. Alternatively, they can use "Clear All Data" to remove all stored sessions, processes, and steps — useful for privacy or when starting from scratch.

**Why this priority**: Data management and error recovery are essential for user trust. Without the ability to discard mistakes, users would hesitate to use the tool for fear of polluting their process library.

**Independent Test**: Can be tested by starting observation, capturing a few steps, clicking "Discard & Restart," and verifying the session data is deleted and a new session begins. For "Clear All," verify the data store is empty afterward.

**Acceptance Scenarios**:

1. **Given** observation is active with captured steps, **When** the user clicks "Discard & Restart," **Then** a confirmation dialog appears, and upon confirmation, the current session's data is deleted, and observation restarts fresh.
2. **Given** multiple observation sessions exist in storage, **When** the user clicks "Clear All Data," **Then** a confirmation dialog appears, and upon confirmation, all sessions, steps, and processes are permanently deleted.
3. **Given** the user cancels the confirmation dialog, **When** prompted for discard or clear, **Then** no data is deleted and the current state is preserved.

---

### User Story 5 - Configure Agent Behavior (Priority: P5)

An IT administrator or power user opens the settings page to configure the agent's behavior: the AI provider endpoint and credentials, screenshot capture interval, excluded applications, PII redaction rules, encryption settings, and data retention policies. Changes take effect immediately or after restarting observation.

**Why this priority**: Configurability is essential for enterprise adoption. Different organizations have different compliance requirements, AI providers, and security policies.

**Independent Test**: Can be tested by opening the settings page, modifying values (e.g., capture interval, adding an excluded app), saving, and verifying the new values are persisted and respected during the next observation session.

**Acceptance Scenarios**:

1. **Given** the user opens the settings page, **When** they modify the AI endpoint and API key, **Then** the next LLM analysis call uses the new configuration.
2. **Given** the user adds an application to the exclusion list, **When** that application is in the foreground during observation, **Then** no capture occurs.
3. **Given** the user changes the capture interval, **When** observation is active, **Then** screenshots are captured at the new interval.
4. **Given** the user enables or modifies PII redaction patterns, **When** a screenshot is captured, **Then** the updated redaction rules are applied before LLM analysis.

### Edge Cases

- What happens when the user rapidly switches between applications faster than the capture interval? The agent should capture on each context change event regardless of interval timing to avoid missing transitions.
- How does the system handle LLM service unavailability? Steps should be captured and stored locally with a "pending analysis" status, and retried when the service recovers.
- What happens when the screenshot contains only a blank screen or desktop wallpaper? The LLM should return a low-confidence result or a "no meaningful activity" classification, and the step should be recorded but de-prioritized.
- How does the system handle very long observation sessions (hours)? Memory usage should remain bounded by processing and discarding raw screenshots after analysis, retaining only metadata and compressed thumbnails.
- What happens when disk space is critically low? The agent should warn the user and gracefully pause observation rather than crashing or losing data silently.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST capture screenshots of the foreground window at a configurable interval (default: 5 seconds).
- **FR-002**: System MUST detect foreground application changes and capture immediately on context switch.
- **FR-003**: System MUST detect system idle, lock, and screensaver states and automatically pause observation.
- **FR-004**: System MUST redact PII (emails, phone numbers, SSNs, credit card numbers, IP addresses) from screenshots before sending to the LLM.
- **FR-005**: System MUST support a configurable keyword blocklist for text redaction in captured window titles and application names.
- **FR-006**: System MUST support a configurable list of excluded applications that are never captured.
- **FR-007**: System MUST send redacted screenshots to an LLM vision endpoint and receive structured analysis results including process name, high-level action, low-level step, user intent, and confidence score.
- **FR-008**: System MUST group analyzed steps into business processes based on process name similarity and temporal proximity (configurable time gap threshold).
- **FR-009**: System MUST persist observation sessions, process steps, and discovered business processes to local encrypted storage.
- **FR-010**: System MUST encrypt all persisted observation data using AES-256-GCM with keys protected by the operating system's credential store.
- **FR-011**: System MUST provide an append-only audit log of all observation events (start, stop, capture, analysis, redaction actions).
- **FR-012**: System MUST allow users to discard the current observation session and restart, deleting all data from the discarded session.
- **FR-013**: System MUST allow users to clear all stored data (sessions, steps, processes) with explicit confirmation.
- **FR-014**: System MUST run in the system tray with start/stop observation controls accessible from both the tray menu and the main window.
- **FR-015**: System MUST display a live activity feed showing captured events in real time during observation.
- **FR-016**: System MUST provide a process viewer with a two-pane layout: process list and step detail.
- **FR-017**: System MUST provide a settings interface for AI configuration, observation parameters, and compliance controls.
- **FR-018**: System MUST NOT send any data to external services without it first passing through the complete compliance pipeline (exclusion check → redaction → then external call).

### Key Entities

- **Observation Session**: A bounded period of user observation with a start time, end time, and status. Contains multiple process steps. Represents one "recording" of user activity.
- **Process Step**: A single observed action at a point in time. Attributes: timestamp, application name, window title, screenshot reference, high-level description, low-level detail, user intent, confidence score, analysis status. Belongs to one session and one business process.
- **Business Process**: A discovered workflow composed of ordered steps sharing a common process name. Attributes: name, description, step count, first/last seen timestamps. May span multiple observation sessions.
- **Application Context**: The foreground application's identity at a point in time: process name, window title, executable path. Used for exclusion checks and context change detection.
- **Screen Capture**: The raw screenshot data (compressed image) along with metadata about capture trigger (interval vs. context change). Ephemeral — discarded after analysis unless configured otherwise.
- **Compliance Settings**: User/admin-configurable rules governing redaction patterns, excluded applications, encryption mode, retention period, and audit logging preferences.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can start observation, perform a 5-minute business process across 3+ applications, stop observation, and see the process correctly identified with ordered steps — within a single session, end to end.
- **SC-002**: All PII in screenshots (email addresses, phone numbers, financial data) is redacted before leaving the local machine, verified by inspecting redacted images.
- **SC-003**: The system captures at least 90% of application context switches during normal desktop usage without the user having to manually trigger captures.
- **SC-004**: Process step analysis results are available within 10 seconds of screenshot capture under normal network conditions.
- **SC-005**: The process viewer displays all discovered processes and their steps correctly, with no data loss between capture and display.
- **SC-006**: The system operates continuously for 2+ hours of observation without degradation in memory usage, capture accuracy, or responsiveness.
- **SC-007**: An enterprise administrator can configure exclusion lists, redaction rules, and encryption settings through the settings interface without modifying any files or code.

## Assumptions

- Users are running Windows 10 or later with desktop applications (not remote desktop or virtual machines in the initial version).
- An Azure OpenAI endpoint with a vision-capable model (GPT-4o or equivalent) is available and the user has valid credentials.
- Screenshots are captured of the foreground window only (not the entire screen) to minimize data exposure.
- The LLM's structured output format for process analysis is reliable enough for automated grouping; low-confidence results are flagged rather than discarded.
- The application runs with standard user permissions — no administrator elevation required for core functionality.
