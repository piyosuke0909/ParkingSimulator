from fastapi import APIRouter

from services.state_store import store

router = APIRouter(prefix="/api/parking", tags=["parking"])


@router.get("/status")
def get_status() -> dict:
    return store.parking_status()


@router.get("/recommendation")
def get_recommendation(userSessionId: str = "demo-user") -> dict:
    return store.recommendation(userSessionId)
