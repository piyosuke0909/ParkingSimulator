import hmac
import os

from fastapi import Header, HTTPException


def configured_api_key() -> str:
    return os.getenv("SMARTPARKING_LOCAL_API_KEY", "local-dev-key").strip()


def require_local_api_key(
    x_api_key: str | None = Header(default=None, alias="X-API-Key"),
) -> None:
    expected = configured_api_key()
    if not expected or not x_api_key or not hmac.compare_digest(x_api_key, expected):
        raise HTTPException(status_code=401, detail="X-API-Key is missing or invalid")
