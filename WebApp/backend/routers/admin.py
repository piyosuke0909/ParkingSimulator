import os

from fastapi import APIRouter, HTTPException, status

from models.schemas import AiRecommendationRequest
from models.v1 import CreateAreaPolicyCommandRequest
from services.command_store import CommandConflictError, command_store
from services.state_store import AREA_MASTER, store

router = APIRouter(prefix="/api/admin", tags=["admin"])


@router.get("/state")
def get_admin_state() -> dict:
    state = store.admin_state()
    command_state = command_store.admin_summary()
    target = command_state["commandTarget"]
    return {
        **state,
        **command_state,
        "commandTargetMatchesSnapshot": bool(
            target
            and store.snapshot_matches_command_target(
                target["sessionId"],
                target["runId"],
            )
        ),
    }


@router.get("/areas")
def get_areas() -> dict:
    return {"areas": AREA_MASTER}


@router.get("/ai/status")
def get_ai_status() -> dict:
    api_key = os.getenv("GEMINI_API_KEY", "")
    model = os.getenv("GEMINI_MODEL", "gemini-3.5-flash")
    return {
        "provider": "gemini",
        "model": model,
        "configured": bool(api_key.strip()),
        "mode": "gemini" if api_key.strip() else "fallback",
    }


@router.post("/ai/recommendations")
def generate_ai_recommendation(request: AiRecommendationRequest) -> dict:
    return store.generate_ai_recommendation(request.instruction)


@router.post("/commands", status_code=status.HTTP_201_CREATED)
def create_command(request: CreateAreaPolicyCommandRequest) -> dict:
    target = command_store.current_target()
    if not target:
        raise HTTPException(
            status_code=409,
            detail="UnityのCommand受信接続を確認できないためCommandを作成できません。",
        )
    if not store.snapshot_matches_command_target(target.session_id, target.run_id):
        raise HTTPException(
            status_code=409,
            detail="表示中のUnity状態とCommand送信先が一致しません。最新の接続状態を確認してください。",
        )
    try:
        command = command_store.create(request, target)
    except CommandConflictError as exc:
        raise HTTPException(status_code=409, detail=str(exc)) from exc
    store.add_log(
        "admin_command_created",
        f"{request.payload.areaId}エリアを{request.payload.policy}にするCommandを作成しました。",
        {"commandId": command["commandId"], "commandType": request.commandType},
    )
    return {"command": command}
