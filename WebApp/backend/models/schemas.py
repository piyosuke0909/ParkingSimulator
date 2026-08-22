from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field


RiskLevel = Literal["low", "medium", "high"]


class Vector3(BaseModel):
    x: float
    y: float = 0
    z: float


class SnapshotSummary(BaseModel):
    empty: int = 0
    reserved: int = 0
    occupied: int = 0
    leaving: int = 0
    disabled: int = 0


class ParkingSlotSnapshot(BaseModel):
    slotId: str
    areaId: str
    state: str = "Unknown"
    sensorOccupied: bool = False
    isLeaving: bool = False
    position: Vector3 | None = None
    parkingPoint: Vector3 | None = None
    accessWaypointId: str | None = None
    reservedByCarId: str | None = None
    occupiedByCarId: str | None = None


class CarSnapshot(BaseModel):
    carId: str
    state: str = "Unknown"
    position: Vector3 | None = None
    targetSlotId: str | None = None
    targetAreaId: str | None = None
    isStoppedByFrontCar: bool = False


class WaypointSnapshot(BaseModel):
    waypointId: str
    name: str | None = None
    position: Vector3
    nextWaypointIds: list[str] = Field(default_factory=list)
    isEntrance: bool = False
    isExit: bool = False
    isIntersection: bool = False
    isStopPoint: bool = False


class UnitySnapshot(BaseModel):
    sourceId: str = "unity-webgl-admin-01"
    scene: str = "Unknown"
    sessionId: str | None = None
    runId: str | None = None
    sequenceNumber: int = Field(ge=0)
    timestamp: datetime
    summary: SnapshotSummary = Field(default_factory=SnapshotSummary)
    slots: list[ParkingSlotSnapshot] = Field(default_factory=list)
    cars: list[CarSnapshot] = Field(default_factory=list)
    waypoints: list[WaypointSnapshot] = Field(default_factory=list)


class GuidanceStartRequest(BaseModel):
    userSessionId: str
    targetAreaId: str | None = None


class GuidanceCancelRequest(BaseModel):
    userSessionId: str
    reservationId: str | None = None


class AiRecommendationRequest(BaseModel):
    instruction: str = "混雑エリアを避けて入場車を誘導し、警備員配置を提案してください。"
