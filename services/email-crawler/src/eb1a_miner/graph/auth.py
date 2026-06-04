from __future__ import annotations

import logging
import time
from pathlib import Path

import msal

log = logging.getLogger(__name__)

_RESERVED_SCOPES = {"openid", "profile", "offline_access"}
DEFAULT_SCOPES = ["User.Read", "Mail.Read"]


def _cache_path(settings) -> Path:
    settings.cache_dir.mkdir(parents=True, exist_ok=True)
    return Path(settings.cache_dir) / "msal_token_cache.bin"


def _sanitize_scopes(scopes: list[str]) -> list[str]:
    # MSAL Python forbids requesting reserved scopes explicitly
    cleaned = [s for s in scopes if s not in _RESERVED_SCOPES]
    # de-dupe preserving order
    seen: set[str] = set()
    out: list[str] = []
    for s in cleaned:
        if s not in seen:
            out.append(s)
            seen.add(s)
    return out


def get_access_token(
    settings,
    scopes: list[str] | None = None,
    *,
    force_reauth: bool = False,
    device_poll_timeout_sec: int = 900,
) -> str:
    """
    Returns a Graph access token. Uses cache+silent when possible.
    Falls back to device code flow only when needed.

    IMPORTANT: Do NOT assume token is a JWT. Some tokens are opaque and still valid.
    """
    if not settings.ms_client_id:
        raise RuntimeError("MS_CLIENT_ID is not set. Put it in .env as MS_CLIENT_ID=...")

    scopes = _sanitize_scopes(scopes or DEFAULT_SCOPES)
    authority = f"https://login.microsoftonline.com/{getattr(settings, 'ms_tenant', 'common')}"

    cache = msal.SerializableTokenCache()
    cache_file = _cache_path(settings)

    if cache_file.exists() and not force_reauth:
        try:
            cache.deserialize(cache_file.read_text(encoding="utf-8"))
        except Exception:
            # corrupt cache, wipe it
            try:
                cache_file.unlink()
            except Exception:
                pass

    app = msal.PublicClientApplication(
        client_id=settings.ms_client_id,
        authority=authority,
        token_cache=cache,
    )

    # 1) Silent first (this is how you avoid authorizing every run)
    if not force_reauth:
        accounts = app.get_accounts()
        if accounts:
            result = app.acquire_token_silent(scopes, account=accounts[0])
            if isinstance(result, dict) and result.get("access_token"):
                return result["access_token"]

    # 2) Device code flow
    flow = app.initiate_device_flow(scopes=scopes)
    if "user_code" not in flow:
        raise RuntimeError(f"Failed to start device flow: {flow}")

    print(flow["message"])

    interval = int(flow.get("interval", 5))
    expires_in = int(flow.get("expires_in", device_poll_timeout_sec))
    deadline = time.time() + min(expires_in, device_poll_timeout_sec)

    while True:
        result = app.acquire_token_by_device_flow(flow)

        if isinstance(result, dict) and result.get("access_token"):
            if cache.has_state_changed:
                cache_file.write_text(cache.serialize(), encoding="utf-8")
            return result["access_token"]

        # Normal while you are still finishing login/consent
        if isinstance(result, dict) and result.get("error") in ("authorization_pending", "slow_down"):
            if time.time() >= deadline:
                raise RuntimeError("Device login timed out. Re-run and enter the code again.")
            if result.get("error") == "slow_down":
                interval += 5
            time.sleep(interval)
            continue

        raise RuntimeError(f"Token request failed: {result}")
