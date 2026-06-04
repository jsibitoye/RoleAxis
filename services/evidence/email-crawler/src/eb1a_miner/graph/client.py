from __future__ import annotations

import requests

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


def graph_get(token: str, path: str, params: dict | None = None, timeout: int = 60) -> dict:
    url = f"{GRAPH_ROOT}{path}"
    r = requests.get(
        url,
        headers={"Authorization": f"Bearer {token}"},
        params=params,
        timeout=timeout,
    )
    if not r.ok:
        _raise_graph_error(r)
    return r.json()


def graph_get_with_auth(settings, path: str, params: dict | None = None, timeout: int = 60) -> dict:
    """
    Calls Graph using a token from get_access_token(settings).
    If token is invalid/expired and Graph returns 401, it retries once after forcing re-auth.
    """
    from eb1a_miner.graph.auth import get_access_token  # local import to avoid cycles

    token = get_access_token(settings)
    try:
        return graph_get(token, path, params=params, timeout=timeout)
    except RuntimeError as e:
        msg = str(e)
        if "Graph error 401" not in msg:
            raise

        # Force re-auth and retry once
        token = get_access_token(settings, force_reauth=True)
        return graph_get(token, path, params=params, timeout=timeout)
