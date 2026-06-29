import os

from fastapi import APIRouter

from models.schemas import AiRecommendationRequest
from services.state_store import AREA_MASTER, store

router = APIRouter(prefix="/api/admin", tags=["admin"])


@router.get("/state")
def get_admin_state() -> dict:
    return store.admin_state()


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
