from __future__ import annotations

import hmac
from typing import Optional

from fastapi import Header, HTTPException

from app.config import settings


def require_api_key(x_api_key: Optional[str] = Header(default=None, alias="X-API-Key")) -> None:
    if not settings.require_api_key:
        return
    if not settings.api_key:
        raise HTTPException(status_code=500, detail={"error": {"code": "API_KEY_NOT_CONFIGURED", "message": "Backend API key is not configured."}})
    if not x_api_key or not hmac.compare_digest(x_api_key, settings.api_key):
        raise HTTPException(status_code=401, detail={"error": {"code": "INVALID_API_KEY", "message": "X-API-Key is missing or invalid."}})
