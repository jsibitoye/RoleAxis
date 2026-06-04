from __future__ import annotations

import re
from typing import Any

import requests
from bs4 import BeautifulSoup

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


def _get_json(token: str, path: str, params: dict[str, Any] | None = None, timeout: int = 60) -> dict:
    url = f"{GRAPH_ROOT}{path}"
    headers = {"Authorization": f"Bearer {token}"}
    resp = requests.get(url, headers=headers, params=params, timeout=timeout)
    if not resp.ok:
        _raise_graph_error(resp)
    return resp.json()


def html_to_text(html: str) -> str:
    soup = BeautifulSoup(html, "html.parser")

    # remove scripts/styles
    for tag in soup(["script", "style", "noscript"]):
        tag.decompose()

    text = soup.get_text("\n", strip=True)

    # normalize whitespace
    text = re.sub(r"[ \t]+", " ", text)
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text.strip()


def get_full_message(
    token: str,
    message_id: str,
    *,
    select: str = "id,conversationId,subject,receivedDateTime,from,toRecipients,ccRecipients,webLink,body",
) -> dict:
    return _get_json(token, f"/me/messages/{message_id}", {"$select": select})


def get_full_body_text(token: str, message_id: str) -> tuple[str, str]:
    """
    Returns (content_type, text) where content_type is 'text' or 'html' from Graph.
    We always return a plain-text version in text.
    """
    data = get_full_message(token, message_id, select="id,body")
    body = data.get("body") or {}
    ctype = (body.get("contentType") or "").lower()
    content = body.get("content") or ""

    if not content:
        return (ctype or "text", "")

    if ctype == "html":
        return ("html", html_to_text(content))

    return ("text", str(content).strip())
