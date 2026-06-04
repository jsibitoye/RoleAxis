from __future__ import annotations

import logging
from dataclasses import dataclass
from datetime import datetime
from typing import Any, Dict, List, Optional
import requests

from eb1a_miner.graph.client import GRAPH_ROOT

log = logging.getLogger(__name__)


@dataclass
class ThreadPreview:
    id: str
    subject: str
    from_name: str
    from_email: str
    received_dt: Optional[datetime]
    body_preview: str


def _raise_graph_error(resp: requests.Response) -> None:
    try:
        j = resp.json()
        err = j.get("error", {}) if isinstance(j, dict) else {}
        code = err.get("code", "Unknown")
        msg = err.get("message", resp.text)
    except Exception:
        code = "Unknown"
        msg = resp.text
    raise RuntimeError(f"Graph error {resp.status_code} {code}: {msg}")


def _get_json(token: str, url: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    r = requests.get(
        url,
        headers={"Authorization": f"Bearer {token}"},
        params=params or {},
        timeout=60,
    )
    if not r.ok:
        _raise_graph_error(r)
    return r.json()


def _parse_dt(s: Optional[str]) -> Optional[datetime]:
    if not s:
        return None
    # Graph returns ISO 8601 like 2026-01-11T16:02:36Z or with offset
    try:
        if s.endswith("Z"):
            s = s.replace("Z", "+00:00")
        return datetime.fromisoformat(s)
    except Exception:
        return None


def get_message_conversation_id(token: str, message_id: str) -> Optional[str]:
    """
    Fetch conversationId for a message. This is cheap and Graph-friendly.
    """
    url = f"{GRAPH_ROOT}/me/messages/{message_id}"
    data = _get_json(token, url, params={"$select": "conversationId"})
    return data.get("conversationId")


def fetch_thread_previews(token: str, conversation_id: str, max_messages: int = 25) -> List[ThreadPreview]:
    """
    Fetch up to max_messages in a thread using ONLY:
      $filter=conversationId eq '{id}'
      $select=...
      $top=...
    No $orderby, no extra filters. This avoids InefficientFilter.
    If Graph still refuses (some tenants do), the caller should catch and fallback.
    """
    if not conversation_id:
        return []

    url = f"{GRAPH_ROOT}/me/messages"

    # Keep it minimal. The more fields you ask for, the more Graph struggles.
    select_fields = ",".join(
        [
            "id",
            "subject",
            "receivedDateTime",
            "from",
            "bodyPreview",
        ]
    )

    # IMPORTANT: no $orderby here. Graph can return in arbitrary order; we can sort client-side if needed.
    params = {
        "$filter": f"conversationId eq '{conversation_id}'",
        "$select": select_fields,
        "$top": str(int(max_messages)),
    }

    data = _get_json(token, url, params=params)
    items = data.get("value", []) or []

    out: List[ThreadPreview] = []
    for it in items:
        frm = it.get("from", {}) or {}
        email_addr = (frm.get("emailAddress", {}) or {})
        out.append(
            ThreadPreview(
                id=str(it.get("id", "")),
                subject=str(it.get("subject", "") or ""),
                from_name=str(email_addr.get("name", "") or ""),
                from_email=str(email_addr.get("address", "") or ""),
                received_dt=_parse_dt(it.get("receivedDateTime")),
                body_preview=str(it.get("bodyPreview", "") or ""),
            )
        )

    # Optional: sort client-side by received_dt desc to make it coherent
    out.sort(key=lambda x: x.received_dt or datetime.min, reverse=True)
    return out
