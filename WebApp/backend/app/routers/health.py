from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy import text
from sqlalchemy.orm import Session

from app.auth import require_api_key
from app.contracts import iso_z
from app.db import get_db

router = APIRouter(tags=["health"])


@router.get("/api/health", dependencies=[Depends(require_api_key)])
def health(db: Session = Depends(get_db)) -> dict:
    db.execute(text("SELECT 1"))
    return {"status": "ok", "service": "smartparking-p3-backend", "serverTimeUtc": iso_z()}
