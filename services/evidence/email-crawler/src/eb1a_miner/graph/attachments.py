from __future__ import annotations

import base64
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator

import requests

GRAPH_ROOT = "https://graph.microsoft.com/v1.0"


@dataclass(frozen=True)
class AttachmentInfo:
    id: str
    name: str | None
    content_type: str | None
    size: int | None
    is_inline: bool
    # best-effort: list endpoint does not allow @odata.type in $select
    odata_type: str | None = None


_FILENAME_BAD = re.compile(r'[<>:"/\\|?*\x00-\x1F]+')


def _safe_filename(name: str | None, fallback: str = "attachment.bin") -> str:
    n = (name or "").strip()
    if not n:
        return fallback
    n = _FILENAME_BAD.sub("_", n).strip(" .")
    return n or fallback


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


def iter_attachments(token: str, message_id: str, *, top: int = 50) -> Iterator[AttachmentInfo]:
    url = f"{GRAPH_ROOT}/me/messages/{message_id}/attachments"
    # IMPORTANT: Do NOT include @odata.type in $select (Graph rejects it).
    params = {"$top": top, "$select": "id,name,contentType,size,isInline"}

    while True:
        data = _get_json(token, url, params=params)
        for a in data.get("value", []) or []:
            if not isinstance(a, dict):
                continue

            # We cannot reliably get @odata.type here.
            # contentType exists for fileAttachment; itemAttachment often also exists but contentBytes won't.
            yield AttachmentInfo(
                id=str(a.get("id", "")),
                name=a.get("name"),
                content_type=a.get("contentType"),
                size=a.get("size"),
                is_inline=bool(a.get("isInline", False)),
                odata_type=None,
            )

        nxt = data.get("@odata.nextLink")
        if not nxt:
            return
        url = nxt
        params = None


def download_file_attachments(
    token: str,
    settings,
    message_id: str,
    *,
    include_inline: bool = False,
) -> list[Path]:
    """
    Downloads file attachments for a message into:
      <attachments_dir>/<message_id>/<filename>

    Only downloads attachments that actually have `contentBytes`
    (fileAttachment). Skips itemAttachment/referenceAttachment automatically.
    """
    attachments_dir: Path = getattr(settings, "attachments_dir", Path("data/attachments")).resolve()
    msg_dir = attachments_dir / message_id
    msg_dir.mkdir(parents=True, exist_ok=True)

    out: list[Path] = []

    for info in iter_attachments(token, message_id):
        if info.is_inline and not include_inline:
            continue

        # Fetch full attachment object to determine type and contentBytes
        url = f"{GRAPH_ROOT}/me/messages/{message_id}/attachments/{info.id}"
        data = _get_json(token, url)

        odata_type = data.get("@odata.type")
        if odata_type != "#microsoft.graph.fileAttachment":
            # itemAttachment / referenceAttachment etc.
            continue

        content_b64 = data.get("contentBytes")
        if not content_b64:
            continue

        filename = _safe_filename(data.get("name") or info.name, fallback=f"{info.id}.bin")
        path = msg_dir / filename

        raw = base64.b64decode(content_b64)
        path.write_bytes(raw)
        out.append(path)

    return out
