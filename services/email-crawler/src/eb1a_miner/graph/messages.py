from __future__ import annotations

from typing import Any, Dict
import requests

from eb1a_miner.graph.client import graph_get_with_auth

GRAPH_ROOT = "https://graph.microsoft.com/v1.0"


def _raise_graph_error(resp: requests.Response) -> None:
    try:
        data = resp.json()
    except Exception:
        resp.raise_for_status()
        return

    if resp.ok:
        return

    err = data.get("error") if isinstance(data, dict) else None
    if isinstance(err, dict):
        code = err.get("code")
        msg = err.get("message")
        raise RuntimeError(f"Graph error {resp.status_code} {code}: {msg}")
    raise RuntimeError(f"Graph error {resp.status_code}: {data}")


def graph_get_raw(token: str, path: str, params: dict | None = None, timeout: int = 60) -> dict:
    url = f"{GRAPH_ROOT}{path}"
    headers = {"Authorization": f"Bearer {token}"}
    resp = requests.get(url, headers=headers, params=params, timeout=timeout)
    if not resp.ok:
        _raise_graph_error(resp)
    return resp.json()


def get_message(token: str, message_id: str, *, select: str | None = None) -> dict:
    params = {"$select": select} if select else None
    return graph_get_raw(token, f"/me/messages/{message_id}", params=params)

def get_message_body_preview(settings, message_id: str) -> dict:
    # bodyPreview is plain text; body is html/text with contentType + content
    sel = "id,subject,receivedDateTime,from,toRecipients,ccRecipients,hasAttachments,bodyPreview,body,webLink"
    return graph_get_with_auth(settings, f"/me/messages/{message_id}", {"$select": sel})