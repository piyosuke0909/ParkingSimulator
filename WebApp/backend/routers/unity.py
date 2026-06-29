from fastapi import APIRouter

from models.schemas import UnitySnapshot
from services.state_store import store

router = APIRouter(prefix="/api/unity", tags=["unity"])


@router.post("/snapshot")
def receive_snapshot(snapshot: UnitySnapshot) -> dict:
    return store.ingest_snapshot(snapshot)
