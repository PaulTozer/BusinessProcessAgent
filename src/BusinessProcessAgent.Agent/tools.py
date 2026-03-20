"""Tools for querying the BusinessProcessAgent PostgreSQL database.

Each tool is a plain async function with annotated parameters so the
Microsoft Agent Framework can expose them to the LLM automatically.
"""

from __future__ import annotations

import json
import os
from typing import Annotated

import asyncpg


async def _get_pool() -> asyncpg.Pool:
    """Return (and lazily create) a connection pool for the shared database."""
    global _pool
    if _pool is None:
        _pool = await asyncpg.create_pool(
            dsn=os.environ["BPA_DATABASE_URL"],
            min_size=1,
            max_size=5,
        )
    return _pool


_pool: asyncpg.Pool | None = None


async def _query(sql: str, *args) -> list[dict]:
    """Execute a read-only query and return rows as dicts."""
    pool = await _get_pool()
    async with pool.acquire() as conn:
        rows = await conn.fetch(sql, *args)
        return [dict(row) for row in rows]


def _serialize(obj):
    """JSON serializer that handles datetime objects from PostgreSQL."""
    from datetime import datetime, date
    if isinstance(obj, (datetime, date)):
        return obj.isoformat()
    raise TypeError(f"Object of type {type(obj)} is not JSON serializable")


# ── Tools exposed to the agent ────────────────────────────────────


async def list_business_processes() -> str:
    """List all discovered business processes with observation counts and date ranges.

    Returns a JSON array of business processes including name, description,
    times observed, and first/last seen timestamps.  Use this to get an
    overview of the workflows that have been captured.
    """
    rows = await _query(
        """
        SELECT id, name, description, first_seen, last_seen, times_observed
        FROM business_processes
        ORDER BY times_observed DESC
        """
    )
    if not rows:
        return json.dumps({"message": "No business processes have been recorded yet."})
    return json.dumps(rows, indent=2, default=_serialize)


async def get_process_steps(
    process_name: Annotated[
        str,
        "The exact business process name to retrieve steps for (from list_business_processes).",
    ],
) -> str:
    """Get the detailed step-by-step breakdown of a specific business process.

    Returns every observed step: application used, window context, high-level
    and low-level actions, user intent, confidence score, and timestamps.
    Use this to deeply understand *how* a process is actually performed.
    """
    rows = await _query(
        """
        SELECT id, session_id, timestamp, application_name, window_title,
               high_level_action, low_level_action, user_intent,
               business_process_name, step_number, additional_context, confidence
        FROM process_steps
        WHERE business_process_name = $1
        ORDER BY timestamp ASC
        """,
        process_name,
    )
    if not rows:
        return json.dumps({"message": f"No steps found for process '{process_name}'."})
    return json.dumps(rows, indent=2, default=_serialize)


async def get_recent_activity(
    limit: Annotated[int, "Maximum number of recent steps to return (default 30)."] = 30,
) -> str:
    """Get the most recent observed activity across all sessions.

    Returns the latest process steps in reverse chronological order.
    Useful for understanding what the user has been working on recently.
    """
    rows = await _query(
        """
        SELECT id, session_id, timestamp, application_name, window_title,
               high_level_action, low_level_action, user_intent,
               business_process_name, step_number, confidence
        FROM process_steps
        ORDER BY timestamp DESC
        LIMIT $1
        """,
        limit,
    )
    if not rows:
        return json.dumps({"message": "No activity has been recorded yet."})
    rows.reverse()  # chronological order
    return json.dumps(rows, indent=2, default=_serialize)


async def get_session_summary(
    session_id: Annotated[str, "The session ID to summarise."],
) -> str:
    """Get a full summary of a single observation session including all its steps.

    Returns the session metadata (start time, end time, step count) and all
    process steps recorded during the session.
    """
    sessions = await _query(
        "SELECT * FROM observation_sessions WHERE id = $1", session_id
    )
    if not sessions:
        return json.dumps({"message": f"Session '{session_id}' not found."})

    steps = await _query(
        """
        SELECT timestamp, application_name, window_title,
               high_level_action, low_level_action, user_intent,
               business_process_name, step_number, confidence
        FROM process_steps
        WHERE session_id = $1
        ORDER BY timestamp ASC
        """,
        session_id,
    )
    return json.dumps({"session": sessions[0], "steps": steps}, indent=2, default=_serialize)


async def list_sessions(
    limit: Annotated[int, "Maximum sessions to return (default 20)."] = 20,
) -> str:
    """List recent observation sessions with metadata.

    Returns session IDs, start/end times, labels, and step counts.
    Use this to find sessions to drill into.
    """
    rows = await _query(
        """
        SELECT id, started_at, ended_at, label, step_count
        FROM observation_sessions
        ORDER BY started_at DESC
        LIMIT $1
        """,
        limit,
    )
    if not rows:
        return json.dumps({"message": "No observation sessions recorded yet."})
    return json.dumps(rows, indent=2, default=_serialize)


async def get_application_usage_stats() -> str:
    """Get statistics about which applications are used and how often.

    Returns application name, total step count, and distinct business
    processes each application participates in.  Helps identify where
    users spend their time and spot tool-switching overhead.
    """
    rows = await _query(
        """
        SELECT application_name,
               COUNT(*) AS step_count,
               COUNT(DISTINCT business_process_name) AS distinct_processes,
               MIN(timestamp) AS first_seen,
               MAX(timestamp) AS last_seen
        FROM process_steps
        GROUP BY application_name
        ORDER BY step_count DESC
        """
    )
    if not rows:
        return json.dumps({"message": "No application usage data available."})
    return json.dumps(rows, indent=2, default=_serialize)


async def find_process_bottlenecks(
    process_name: Annotated[
        str,
        "The business process name to analyse for bottlenecks.",
    ],
) -> str:
    """Analyse a business process for potential bottlenecks and inefficiencies.

    Returns per-step timing gaps, application switches, low-confidence steps,
    and repeated actions — raw data the agent uses to reason about improvements.
    """
    rows = await _query(
        """
        SELECT step_number, timestamp, application_name, window_title,
               high_level_action, low_level_action, confidence
        FROM process_steps
        WHERE business_process_name = $1
        ORDER BY timestamp ASC
        """,
        process_name,
    )
    if len(rows) < 2:
        return json.dumps({"message": "Not enough steps to analyse bottlenecks."})

    analysis: list[dict] = []
    for i in range(1, len(rows)):
        prev, curr = rows[i - 1], rows[i]
        analysis.append(
            {
                "from_step": prev["step_number"],
                "to_step": curr["step_number"],
                "from_action": prev["high_level_action"],
                "to_action": curr["high_level_action"],
                "from_app": prev["application_name"],
                "to_app": curr["application_name"],
                "app_switch": prev["application_name"] != curr["application_name"],
                "from_timestamp": prev["timestamp"],
                "to_timestamp": curr["timestamp"],
                "confidence": curr["confidence"],
            }
        )

    low_confidence = [r for r in rows if r["confidence"] < 0.6]

    return json.dumps(
        {
            "transitions": analysis,
            "total_steps": len(rows),
            "app_switches": sum(1 for a in analysis if a["app_switch"]),
            "low_confidence_steps": low_confidence,
        },
        indent=2,
        default=_serialize,
    )


# Convenience list for the agent tools= kwarg
ALL_TOOLS = [
    list_business_processes,
    get_process_steps,
    get_recent_activity,
    get_session_summary,
    list_sessions,
    get_application_usage_stats,
    find_process_bottlenecks,
]
