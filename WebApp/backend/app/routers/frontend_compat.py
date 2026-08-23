from __future__ import annotations

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session

from app.api_models import CreateCommandRequest, SetAreaPolicyPayload
from app.auth import require_api_key
from app.db import get_db
from app.errors import error_response
from app.services.commands import CommandError, create_command
from app.services.frontend_views import admin_state, frontend_command_context, frontend_command_view, parking_status, recommendation

router = APIRouter(tags=["frontend-compat"], dependencies=[Depends(require_api_key)])


class FrontendCommandRequest(BaseModel):
    commandType: str = "SET_AREA_POLICY"
    idempotencyKey: str = Field(min_length=1, max_length=220)
    priority: int = 0
    expiresInSeconds: int = Field(default=30, ge=1, le=300)
    reason: str | None = None
    payload: SetAreaPolicyPayload


@router.get("/api/admin/state")
def get_admin_state(db: Session = Depends(get_db)):
    state = admin_state(db)
    db.commit()
    return state


@router.post("/api/admin/commands")
def post_frontend_command(body: FrontendCommandRequest, db: Session = Depends(get_db)):
    if body.commandType != "SET_AREA_POLICY":
        return error_response(422, "UNKNOWN_COMMAND_TYPE", "Only SET_AREA_POLICY is supported by Command v1.0.")

    snapshot_row, target = frontend_command_context(db)
    if snapshot_row is None:
        return error_response(409, "SNAPSHOT_NOT_AVAILABLE", "A current Unity Snapshot has not been received yet.")
    if target is None:
        return error_response(409, "COMMAND_TARGET_UNAVAILABLE", "No active Unity Command target matches the latest Snapshot session/run.")

    request = CreateCommandRequest(
        targetSourceId=target.source_id,
        targetSessionId=target.session_id,
        targetRunId=target.run_id,
        commandType="SET_AREA_POLICY",
        priority=body.priority,
        issuedBy="frontend-admin",
        reason=body.reason,
        expiresInSeconds=body.expiresInSeconds,
        payload=body.payload,
    )
    try:
        command, created = create_command(db, request, body.idempotencyKey.strip())
        db.commit()
        return JSONResponse(
            status_code=201 if created else 200,
            content={"command": frontend_command_view(db, command)},
        )
    except CommandError as exc:
        db.rollback()
        return error_response(exc.status_code, exc.code, exc.message)
    except SQLAlchemyError:
        db.rollback()
        return error_response(503, "COMMAND_STORE_UNAVAILABLE", "Command store is temporarily unavailable.")


@router.get("/api/parking/status")
def get_parking_status(db: Session = Depends(get_db)):
    return parking_status(db)


@router.get("/api/parking/recommendation")
def get_parking_recommendation(userSessionId: str = "demo-user", db: Session = Depends(get_db)):
    _ = userSessionId
    return recommendation(db)


@router.get("/api/admin/ai/status")
def get_ai_status():
    return {"provider": "gemini", "model": "not-configured", "configured": False, "mode": "fallback"}


@router.post("/api/admin/ai/recommendations")
def post_ai_recommendation(db: Session = Depends(get_db)):
    state = admin_state(db)
    db.commit()
    areas = sorted(state.get("areas") or [], key=lambda a: float(a.get("riskScore") or 0), reverse=True)
    target = areas[0] if areas else {"areaId": "A", "label": "Aエリア", "riskLevel": "low"}
    return {
        "generatedAt": state.get("updatedAt"),
        "source": "fallback",
        "title": "現在の駐車場状況に基づく提案",
        "summary": f"{target['label']}を含む混雑状況を確認し、現場判断で案内方針を調整してください。",
        "riskLevel": target.get("riskLevel", "low"),
        "actions": [{"priority": 1, "areaId": target["areaId"], "action": "状況確認", "reason": "最新SnapshotのriskScoreを参照"}],
        "guardAssignments": [],
        "operatorNotes": ["P3 Backendのfallback提案です。Gemini連携は未統合です。"],
        "cached": False,
    }


@router.post("/api/guidance/start")
def guidance_start_not_migrated():
    return error_response(501, "GUIDANCE_NOT_MIGRATED", "Guidance reservation API has not been migrated to the P3 backend yet.")


@router.post("/api/guidance/cancel")
def guidance_cancel_not_migrated():
    return error_response(501, "GUIDANCE_NOT_MIGRATED", "Guidance reservation API has not been migrated to the P3 backend yet.")
