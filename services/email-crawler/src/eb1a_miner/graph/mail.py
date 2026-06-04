from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Iterator

import requests

GRAPH_ROOT = "https://graph.microsoft.com/v1.0"


@dataclass(frozen=True)
class MailAddress:
    name: str | None
    address: str | None


@dataclass(frozen=True)
class Message:
    id: str
    conversation_id: str | None
    subject: str | None
    received_dt: datetime | None
    from_: MailAddress | None
    to: tuple[MailAddress, ...]
    cc: tuple[MailAddress, ...]
    has_attachments: bool
    web_link: str | None
    body_preview: str | None
    folder: str | None  # inbox, sentitems, etc. (what we scanned)


def _parse_graph_dt(value: str | None) -> datetime | None:
    if not value:
        return None
    v = value.strip()
    if v.endswith("Z"):
        v = v[:-1] + "+00:00"
    try:
        dt = datetime.fromisoformat(v)
    except ValueError:
        return None
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt


def _mailaddr(obj: dict | None) -> MailAddress | None:
    if not obj:
        return None
    ea = obj.get("emailAddress") or {}
    return MailAddress(name=ea.get("name"), address=ea.get("address"))


def _mailaddr_list(items: list | None) -> tuple[MailAddress, ...]:
    if not items:
        return tuple()
    out: list[MailAddress] = []
    for x in items:
        m = _mailaddr(x)
        if m:
            out.append(m)
    return tuple(out)


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


def _get_json(token: str, url: str, params: dict | None = None, timeout: int = 60) -> dict:
    headers = {"Authorization": f"Bearer {token}"}
    resp = requests.get(url, headers=headers, params=params, timeout=timeout)
    if not resp.ok:
        _raise_graph_error(resp)
    return resp.json()


def _build_filter(since: datetime | None, until: datetime | None) -> str | None:
    def to_utc_z(dt: datetime) -> str:
        if dt.tzinfo is None:
            dt = dt.replace(tzinfo=timezone.utc)
        dt = dt.astimezone(timezone.utc)
        return dt.strftime("%Y-%m-%dT%H:%M:%SZ")

    clauses: list[str] = []
    if since:
        clauses.append(f"receivedDateTime ge {to_utc_z(since)}")
    if until:
        clauses.append(f"receivedDateTime le {to_utc_z(until)}")

    return " and ".join(clauses) if clauses else None


def iter_messages(
    token: str,
    *,
    top: int = 50,
    since: datetime | None = None,
    until: datetime | None = None,
    folder: str = "inbox",
    select: str = "id,conversationId,subject,receivedDateTime,from,toRecipients,ccRecipients,hasAttachments,webLink,bodyPreview",
    orderby: str = "receivedDateTime desc",
    max_pages: int | None = None,
) -> Iterator[Message]:
    """
    Streams messages from a well-known folder (inbox, sentitems, archive, etc.)
    using pagination. Includes bodyPreview so callers can batch-rank without
    per-message fetches.
    """
    if top <= 0:
        raise ValueError("top must be > 0")

    base_path = f"/me/mailFolders/{folder}/messages"
    url = f"{GRAPH_ROOT}{base_path}"

    params: dict[str, Any] = {
        "$top": top,
        "$select": select,
        "$orderby": orderby,
    }

    filt = _build_filter(since, until)
    if filt:
        params["$filter"] = filt

    pages = 0
    while True:
        data = _get_json(token, url, params=params)
        pages += 1
        if max_pages is not None and pages > max_pages:
            return

        values = data.get("value", [])
        if not isinstance(values, list):
            return

        for m in values:
            if not isinstance(m, dict):
                continue

            yield Message(
                id=str(m.get("id", "")),
                conversation_id=m.get("conversationId"),
                subject=m.get("subject"),
                received_dt=_parse_graph_dt(m.get("receivedDateTime")),
                from_=_mailaddr(m.get("from")),
                to=_mailaddr_list(m.get("toRecipients")),
                cc=_mailaddr_list(m.get("ccRecipients")),
                has_attachments=bool(m.get("hasAttachments", False)),
                web_link=m.get("webLink"),
                body_preview=m.get("bodyPreview"),
                folder=folder,
            )

        next_link = data.get("@odata.nextLink")
        if not next_link:
            return

        url = next_link
        params = None
