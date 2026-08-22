from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from models.schemas import (
    CarSnapshot,
    ParkingSlotSnapshot,
    SnapshotSummary,
    UnitySnapshot,
    Vector3,
)


AreaPolicy = Literal["PRIORITY", "CLOSED", "RESTRICTED", "NORMAL"]
CommandEventType = Literal[
    "command.accepted",
    "command.started",
    "command.succeeded",
    "command.failed",
    "command.rejected",
    "command.expired",
]


class P2ScenarioSnapshot(BaseModel):
    scenarioId: str


class P2RuntimeSnapshot(BaseModel):
    activeVehicleCount: int = 0


class P2SnapshotSummary(BaseModel):
    emptySlotCount: int = 0
    reservedSlotCount: int = 0
    occupiedSlotCount: int = 0
    leavingSlotCount: int = 0
    disabledSlotCount: int = 0


class P2ParkingSlotSnapshot(BaseModel):
    slotId: str
    areaId: str
    sceneAreaId: str = ""
    state: str = "Unknown"
    sensorOccupied: bool = False
    isLeaving: bool = False
    position: Vector3 | None = None
    parkingPointPosition: Vector3 | None = None
    accessWaypointId: str = ""
    reservedByVehicleId: str = ""
    occupiedByVehicleId: str = ""


class P2VehicleSnapshot(BaseModel):
    vehicleId: str
    movementState: str = "Unknown"
    position: Vector3 | None = None
    targetSlotId: str = ""
    targetAreaId: str = ""
    targetSceneAreaId: str = ""
    isStoppedByFrontVehicle: bool = False


class P2SimulationSnapshot(BaseModel):
    model_config = ConfigDict(extra="allow")

    contractName: Literal["smart-parking.simulation-snapshot"]
    schemaVersion: Literal["1.1"]
    snapshotType: Literal["simulation_snapshot"]
    snapshotId: str = Field(min_length=1)
    sourceSystem: Literal["unity"]
    generatedAtUtc: datetime
    sceneName: str = Field(min_length=1)
    sessionId: str = Field(min_length=1)
    runId: str = Field(min_length=1)
    sequenceNumber: int = Field(ge=1)
    scenario: P2ScenarioSnapshot
    runtime: P2RuntimeSnapshot
    summary: P2SnapshotSummary
    parkingSlots: list[P2ParkingSlotSnapshot] = Field(default_factory=list)
    vehicles: list[P2VehicleSnapshot] = Field(default_factory=list)

    def to_mvp_snapshot(self) -> UnitySnapshot:
        return UnitySnapshot(
            sourceId=self.sourceSystem,
            scene=self.sceneName,
            sessionId=self.sessionId,
            runId=self.runId,
            sequenceNumber=self.sequenceNumber,
            timestamp=self.generatedAtUtc,
            summary=SnapshotSummary(
                empty=self.summary.emptySlotCount,
                reserved=self.summary.reservedSlotCount,
                occupied=self.summary.occupiedSlotCount,
                leaving=self.summary.leavingSlotCount,
                disabled=self.summary.disabledSlotCount,
            ),
            slots=[
                ParkingSlotSnapshot(
                    slotId=slot.slotId,
                    areaId=slot.sceneAreaId or slot.areaId,
                    state=slot.state,
                    sensorOccupied=slot.sensorOccupied,
                    isLeaving=slot.isLeaving,
                    position=slot.position,
                    parkingPoint=slot.parkingPointPosition,
                    accessWaypointId=slot.accessWaypointId or None,
                    reservedByCarId=slot.reservedByVehicleId or None,
                    occupiedByCarId=slot.occupiedByVehicleId or None,
                )
                for slot in self.parkingSlots
            ],
            cars=[
                CarSnapshot(
                    carId=vehicle.vehicleId,
                    state=vehicle.movementState,
                    position=vehicle.position,
                    targetSlotId=vehicle.targetSlotId or None,
                    targetAreaId=vehicle.targetSceneAreaId or vehicle.targetAreaId or None,
                    isStoppedByFrontCar=vehicle.isStoppedByFrontVehicle,
                )
                for vehicle in self.vehicles
            ],
        )


class SetAreaPolicyPayload(BaseModel):
    model_config = ConfigDict(extra="forbid")

    areaId: str = Field(pattern="^[A-D]$")
    policy: AreaPolicy
    effectiveUntilUtc: datetime | None = None
    reason: str | None = Field(default=None, max_length=200)


class CreateAreaPolicyCommandRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    commandType: Literal["SET_AREA_POLICY"] = "SET_AREA_POLICY"
    payload: SetAreaPolicyPayload
    idempotencyKey: str | None = Field(default=None, min_length=1, max_length=200)
    expiresInSeconds: int = Field(default=300, ge=10, le=3600)
    priority: int = 0
    correlationId: str | None = None
    issuedBy: str | None = "admin-web"
    reason: str | None = None


class CommandEnvelope(BaseModel):
    model_config = ConfigDict(extra="forbid")

    contractName: Literal["smart-parking.command"] = "smart-parking.command"
    schemaVersion: Literal["1.0"] = "1.0"
    commandId: str = Field(min_length=1)
    idempotencyKey: str = Field(min_length=1)
    commandType: Literal["SET_AREA_POLICY"] = "SET_AREA_POLICY"
    targetSourceId: str = Field(min_length=1)
    targetSessionId: str = Field(min_length=1)
    targetRunId: str = Field(min_length=1)
    createdAtUtc: datetime
    expiresAtUtc: datetime
    priority: int = 0
    correlationId: str | None = None
    issuedBy: str | None = None
    reason: str | None = None
    payload: SetAreaPolicyPayload


class CommandBatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    contractName: Literal["smart-parking.command-batch"] = "smart-parking.command-batch"
    schemaVersion: Literal["1.0"] = "1.0"
    serverTimeUtc: datetime
    leaseSeconds: int = Field(ge=1)
    commands: list[CommandEnvelope] = Field(default_factory=list, max_length=10)


class P2CommandResultPayload(BaseModel):
    areaId: str = ""
    previousPolicy: str = ""
    appliedPolicy: str = ""


class P2CommandEventPayload(BaseModel):
    idempotencyKey: str = Field(min_length=1)
    commandType: Literal["SET_AREA_POLICY"]
    status: str = Field(min_length=1)
    reasonCode: str = ""
    message: str = ""
    retryable: bool = False
    result: P2CommandResultPayload = Field(default_factory=P2CommandResultPayload)


class P2CommandEvent(BaseModel):
    model_config = ConfigDict(extra="allow")

    contractName: Literal["smart-parking.simulation-event"]
    schemaVersion: Literal["1.0"]
    eventId: str = Field(min_length=1)
    eventType: CommandEventType
    sourceSystem: Literal["unity"]
    generatedAtUtc: datetime
    sceneName: str = Field(min_length=1)
    sessionId: str = Field(min_length=1)
    runId: str = Field(min_length=1)
    sequenceNumber: int = Field(ge=1)
    scenarioId: str = Field(min_length=1)
    simulationTimeSeconds: float = Field(ge=0)
    entityType: Literal["command"]
    entityId: str = Field(min_length=1)
    commandId: str = Field(min_length=1)
    payload: dict

    def command_payload(self) -> P2CommandEventPayload:
        return P2CommandEventPayload.model_validate(self.payload.get("command"))
