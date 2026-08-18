from __future__ import annotations

from typing import Any, Literal

from pydantic import BaseModel, Field


class SetAreaPolicyPayload(BaseModel):
    areaId: Literal["A", "B", "C", "D"]
    policy: Literal["PRIORITY", "CLOSED", "RESTRICTED", "NORMAL"]
    effectiveUntilUtc: str | None = None
    reason: str | None = Field(default=None, max_length=200)


class CreateCommandRequest(BaseModel):
    targetSourceId: str = Field(min_length=1)
    targetSessionId: str = Field(min_length=1)
    targetRunId: str = Field(min_length=1)
    commandType: Literal["SET_AREA_POLICY"] = "SET_AREA_POLICY"
    priority: int = 0
    correlationId: str | None = None
    issuedBy: str | None = None
    reason: str | None = None
    expiresInSeconds: int = Field(default=30, ge=1, le=300)
    payload: SetAreaPolicyPayload


class CommandView(BaseModel):
    commandId: str
    idempotencyKey: str
    commandType: str
    targetSourceId: str
    targetSessionId: str
    targetRunId: str
    createdAtUtc: str
    expiresAtUtc: str
    priority: int
    status: str
    deliveryCount: int
    lastDeliveredAtUtc: str | None
    terminalAtUtc: str | None
    payload: dict[str, Any]
    resultPayload: dict[str, Any] | None
