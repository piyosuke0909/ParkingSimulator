from fastapi import APIRouter, HTTPException

from models.schemas import GuidanceCancelRequest, GuidanceStartRequest
from services.state_store import store

router = APIRouter(prefix="/api/guidance", tags=["guidance"])


@router.post("/start")
def start_guidance(request: GuidanceStartRequest) -> dict:
    try:
        return store.start_guidance(request.userSessionId, request.targetAreaId)
    except ValueError as exc:
        raise HTTPException(status_code=409, detail=str(exc)) from exc


@router.post("/cancel")
def cancel_guidance(request: GuidanceCancelRequest) -> dict:
    return store.cancel_guidance(request.userSessionId, request.reservationId)
