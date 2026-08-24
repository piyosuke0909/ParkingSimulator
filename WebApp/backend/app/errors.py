from __future__ import annotations

from typing import Any

from fastapi.responses import JSONResponse


def error_response(status: int, code: str, message: str, details: list[str] | None = None, extra: dict[str, Any] | None = None) -> JSONResponse:
    error: dict[str, Any] = {"code": code, "message": message}
    if details:
        error["details"] = details
    if extra:
        error.update(extra)
    return JSONResponse(status_code=status, content={"error": error})
