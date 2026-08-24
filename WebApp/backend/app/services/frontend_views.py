from __future__ import annotations

from datetime import UTC, datetime
from typing import Any

from sqlalchemy import select
from sqlalchemy.orm import Session

from app.config import settings
from app.contracts import ensure_utc, iso_z
from app.services.commands import refresh_terminal_states
from app.models import CommandRecord, EventRecord, RuntimeTarget, SnapshotRecord

IMAGE_WIDTH = 637
IMAGE_HEIGHT = 492
WORLD_MIN_X = -180.0
WORLD_MAX_X = 180.0
WORLD_MIN_Z = -150.0
WORLD_MAX_Z = 150.0
MAP_X_SCALE = 1.273
MAP_Y_SCALE = 1.364
FLIP_X = True
FLIP_Z = False

AREA_MASTER = [
    {"areaId": "A", "label": "Aエリア", "displayOrder": 1},
    {"areaId": "B", "label": "Bエリア", "displayOrder": 2},
    {"areaId": "C", "label": "Cエリア", "displayOrder": 3},
    {"areaId": "D", "label": "Dエリア", "displayOrder": 4},
]

DEFAULT_AREA_POLYGONS = {
    "A": {"center": {"x": 203.5, "y": 164.0}, "polygon": [[98.8, 87.0], [308.2, 87.0], [308.2, 241.0], [98.8, 241.0]]},
    "B": {"center": {"x": 203.5, "y": 328.0}, "polygon": [[98.8, 251.0], [308.2, 251.0], [308.2, 405.0], [98.8, 405.0]]},
    "C": {"center": {"x": 433.5, "y": 164.0}, "polygon": [[328.8, 87.0], [538.2, 87.0], [538.2, 241.0], [328.8, 241.0]]},
    "D": {"center": {"x": 433.5, "y": 328.0}, "polygon": [[328.8, 251.0], [538.2, 251.0], [538.2, 405.0], [328.8, 405.0]]},
}


def _utc_now() -> datetime:
    return datetime.now(UTC)


def _as_utc(value: datetime | None) -> datetime | None:
    if value is None:
        return None
    if value.tzinfo is None:
        return value.replace(tzinfo=UTC)
    return value.astimezone(UTC)


def _latest_snapshot_record(db: Session) -> SnapshotRecord | None:
    return db.scalar(select(SnapshotRecord).order_by(SnapshotRecord.received_at_utc.desc(), SnapshotRecord.id.desc()).limit(1))


def _normalize_area_id(value: Any) -> str:
    text = str(value or "").strip().upper()
    if text.startswith("AREA-"):
        text = text[5:]
    return text if text in {"A", "B", "C", "D"} else ""


def _world_to_map(x: float, z: float) -> dict[str, float]:
    norm_x = (x - WORLD_MIN_X) / (WORLD_MAX_X - WORLD_MIN_X)
    norm_z = (z - WORLD_MIN_Z) / (WORLD_MAX_Z - WORLD_MIN_Z)
    if FLIP_X:
        norm_x = 1.0 - norm_x
    if FLIP_Z:
        norm_z = 1.0 - norm_z
    map_x = norm_x * IMAGE_WIDTH
    map_y = norm_z * IMAGE_HEIGHT
    map_x = IMAGE_WIDTH / 2 + (map_x - IMAGE_WIDTH / 2) * MAP_X_SCALE
    map_y = IMAGE_HEIGHT / 2 + (map_y - IMAGE_HEIGHT / 2) * MAP_Y_SCALE
    return {
        "x": round(max(0.0, min(IMAGE_WIDTH, map_x)), 3),
        "y": round(max(0.0, min(IMAGE_HEIGHT, map_y)), 3),
    }


def _empty_state() -> dict[str, Any]:
    areas = []
    for master in AREA_MASTER:
        areas.append(
            {
                **master,
                "capacity": 60,
                "emptyCount": 0,
                "occupiedCount": 0,
                "activeAreaReservations": 0,
                "effectiveAvailable": 0,
                "occupancyRate": 0.0,
                "waitingCars": 0,
                "leavingCars": 0,
                "riskScore": 0.0,
                "riskLevel": "low",
                "layout": DEFAULT_AREA_POLYGONS[master["areaId"]],
            }
        )
    return {
        "snapshotVersion": 0,
        "updatedAt": _utc_now().isoformat().replace("+00:00", "Z"),
        "stale": True,
        "source": None,
        "summary": {"capacity": 240, "emptyCount": 0, "occupiedCount": 0, "occupancyRate": 0.0},
        "areas": areas,
        "slots": [],
        "areaLayout": {"generatedFrom": "default", "image": {"width": IMAGE_WIDTH, "height": IMAGE_HEIGHT}},
    }


def parking_status(db: Session) -> dict[str, Any]:
    row = _latest_snapshot_record(db)
    if row is None:
        return _empty_state()

    snapshot = row.payload
    area_by_id: dict[str, dict[str, Any]] = {}
    for item in snapshot.get("areas") or []:
        area_id = _normalize_area_id(item.get("sceneAreaId") or item.get("areaId"))
        if area_id:
            area_by_id[area_id] = item

    vehicles = snapshot.get("vehicles") or []
    statuses: list[dict[str, Any]] = []
    for master in AREA_MASTER:
        area_id = master["areaId"]
        item = area_by_id.get(area_id, {})
        capacity = int(item.get("totalSlotCount") or 60)
        reserved = int(item.get("reservedSlotCount") or 0)
        occupied_raw = int(item.get("occupiedSlotCount") or 0)
        occupied = min(capacity, occupied_raw + reserved)
        empty = int(item.get("emptySlotCount") if item.get("emptySlotCount") is not None else max(capacity - occupied, 0))
        disabled = int(item.get("disabledSlotCount") or 0)
        available = int(item.get("availableSlotCount") if item.get("availableSlotCount") is not None else max(empty - disabled, 0))
        leaving = int(item.get("leavingSlotCount") or 0)
        waiting = 0
        stopped = 0
        for vehicle in vehicles:
            vehicle_area = _normalize_area_id(vehicle.get("targetSceneAreaId") or vehicle.get("targetAreaId"))
            if vehicle_area != area_id:
                continue
            state = str(vehicle.get("movementState") or "").lower()
            if "wait" in state or "queue" in state:
                waiting += 1
            if bool(vehicle.get("isStoppedByFrontVehicle")):
                stopped += 1
        occupancy_rate = occupied / max(capacity, 1)
        risk_score = occupancy_rate * 100.0 + waiting * 10.0 + leaving * 5.0 + stopped * 15.0
        risk_level = "high" if risk_score >= 90 else "medium" if risk_score >= 70 else "low"
        statuses.append(
            {
                **master,
                "capacity": capacity,
                "emptyCount": max(empty, 0),
                "occupiedCount": max(occupied, 0),
                "activeAreaReservations": 0,
                "effectiveAvailable": max(available, 0),
                "occupancyRate": round(occupancy_rate, 4),
                "waitingCars": waiting,
                "leavingCars": leaving,
                "riskScore": round(risk_score, 2),
                "riskLevel": risk_level,
                "layout": {**DEFAULT_AREA_POLYGONS[area_id], "slotCount": capacity},
            }
        )

    slot_rows: list[dict[str, Any]] = []
    for slot in snapshot.get("parkingSlots") or []:
        scene_area = _normalize_area_id(slot.get("sceneAreaId") or slot.get("areaId"))
        position = slot.get("position") or {}
        map_position = None
        if isinstance(position, dict) and "x" in position and "z" in position:
            try:
                map_position = _world_to_map(float(position["x"]), float(position["z"]))
            except (TypeError, ValueError):
                map_position = None
        slot_rows.append(
            {
                "slotId": str(slot.get("slotId") or ""),
                "areaId": scene_area,
                "state": str(slot.get("state") or "Unknown"),
                "sensorOccupied": bool(slot.get("sensorOccupied")),
                "isLeaving": bool(slot.get("isLeaving")),
                "mapPosition": map_position,
                "accessWaypointId": slot.get("accessWaypointId") or None,
            }
        )

    capacity = sum(area["capacity"] for area in statuses)
    empty = sum(area["emptyCount"] for area in statuses)
    occupied = sum(area["occupiedCount"] for area in statuses)
    received = _as_utc(row.received_at_utc)
    stale = True if received is None else (_utc_now() - received).total_seconds() > settings.target_stale_seconds
    return {
        "snapshotVersion": int(snapshot.get("sequenceNumber") or 0),
        "updatedAt": str(snapshot.get("generatedAtUtc") or ""),
        "stale": stale,
        "source": str(snapshot.get("sourceSystem") or "unity"),
        "summary": {
            "capacity": capacity,
            "emptyCount": empty,
            "occupiedCount": occupied,
            "occupancyRate": round(occupied / max(capacity, 1), 4),
        },
        "areas": statuses,
        "slots": slot_rows,
        "areaLayout": {"generatedFrom": "unity-slots", "image": {"width": IMAGE_WIDTH, "height": IMAGE_HEIGHT}},
    }


def _alerts(status: dict[str, Any]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    if status.get("stale"):
        result.append({"type": "stale_snapshot", "areaId": None, "severity": "high", "message": "Unity snapshot の更新を待っています。"})
    for area in status.get("areas") or []:
        rate = float(area.get("occupancyRate") or 0.0)
        if rate >= 0.95:
            result.append({"type": "congestion", "areaId": area["areaId"], "severity": "high", "message": f"{area['label']}が満車に近い状態です。"})
        elif rate >= 0.85:
            result.append({"type": "congestion", "areaId": area["areaId"], "severity": "medium", "message": f"{area['label']}が混雑しています。"})
    return result


def _event_logs(db: Session, limit: int = 50) -> list[dict[str, Any]]:
    rows = db.scalars(select(EventRecord).order_by(EventRecord.received_at_utc.desc()).limit(limit)).all()
    logs = []
    for row in reversed(rows):
        payload = row.payload or {}
        message = str((payload.get("payload") or {}).get("message") or payload.get("eventType") or row.event_type)
        logs.append(
            {
                "id": row.event_id,
                "timestamp": str(payload.get("generatedAtUtc") or _as_utc(row.received_at_utc).isoformat()),
                "type": row.event_type,
                "message": message,
            }
        )
    return logs


def admin_state(db: Session) -> dict[str, Any]:
    status = parking_status(db)
    snapshot_row, target = frontend_command_context(db)
    command_state = _frontend_command_state(db, snapshot_row, target)
    db.flush()
    return {
        **status,
        "alerts": _alerts(status),
        "guards": [
            {"guardId": "G01", "status": "active", "currentArea": "A", "shift": "09:00-18:00", "break": "13:00-14:00", "canMove": True},
            {"guardId": "G02", "status": "active", "currentArea": "C", "shift": "10:00-19:00", "break": "14:00-15:00", "canMove": True},
        ],
        "logs": _event_logs(db),
        **command_state,
        "lastAiRecommendation": None,
    }


def recommendation(db: Session) -> dict[str, Any]:
    status = parking_status(db)
    areas = status.get("areas") or []
    candidates = [a for a in areas if int(a.get("effectiveAvailable") or 0) > 0]
    if not candidates:
        return {"guidanceLevel": "area", "status": "unavailable", "message": "案内可能なエリアがありません。"}
    selected = sorted(candidates, key=lambda a: (-int(a["effectiveAvailable"]), float(a["riskScore"]), int(a["displayOrder"])))[0]
    return {
        "guidanceLevel": "area",
        "status": "not_started",
        "updatedAt": status.get("updatedAt"),
        "targetArea": {
            "areaId": selected["areaId"],
            "label": selected["label"],
            "reason": f"有効空き {selected['effectiveAvailable']} 台を確認しています。",
        },
        "recommendedSlotId": None,
        "recommendedSlot": None,
        "message": f"{selected['label']}を案内候補として表示しています。",
        "replanReason": None,
        "reservation": None,
        "assignedCar": None,
        "route": None,
        "summary": {
            "emptyCount": selected["emptyCount"],
            "effectiveAvailable": selected["effectiveAvailable"],
            "congestionLevel": selected["riskLevel"],
        },
    }


FRONTEND_COMMAND_STATUS = {
    "CREATED": "pending",
    "DELIVERED": "delivered",
    "ACCEPTED": "accepted",
    "STARTED": "started",
    "SUCCEEDED": "succeeded",
    "FAILED": "failed",
    "REJECTED": "rejected",
    "EXPIRED": "expired",
    "TIMED_OUT": "timed_out",
}


def _fresh_matching_target(db: Session, snapshot_row: SnapshotRecord | None) -> RuntimeTarget | None:
    if snapshot_row is None:
        return None
    targets = db.scalars(
        select(RuntimeTarget)
        .where(RuntimeTarget.session_id == snapshot_row.session_id, RuntimeTarget.run_id == snapshot_row.run_id)
        .order_by(RuntimeTarget.last_poll_at_utc.desc(), RuntimeTarget.source_id.asc())
    ).all()
    now = _utc_now()
    for target in targets:
        last_poll = _as_utc(target.last_poll_at_utc)
        if last_poll is None:
            continue
        if (now - last_poll).total_seconds() <= settings.runtime_online_seconds:
            return target
    return None


def frontend_command_view(db: Session, record: CommandRecord) -> dict[str, Any]:
    last_result = None
    if record.terminal_event_id:
        event = db.scalar(select(EventRecord).where(EventRecord.event_id == record.terminal_event_id))
        if event:
            event_payload = event.payload or {}
            last_result = {
                "eventType": event.event_type,
                "payload": event_payload.get("payload") or {},
            }
    return {
        "commandId": record.command_id,
        "idempotencyKey": record.idempotency_key,
        "commandType": record.command_type,
        "targetSourceId": record.target_source_id,
        "targetSessionId": record.target_session_id,
        "targetRunId": record.target_run_id,
        "createdAtUtc": iso_z(record.created_at_utc),
        "expiresAtUtc": iso_z(record.expires_at_utc),
        "priority": record.priority,
        "payload": record.payload,
        "status": FRONTEND_COMMAND_STATUS.get(record.status, record.status.lower()),
        "deliveryAttempts": record.delivery_count,
        "completedAtUtc": iso_z(record.terminal_at_utc) if record.terminal_at_utc else None,
        "lastResult": last_result,
    }


def frontend_command_context(db: Session) -> tuple[SnapshotRecord | None, RuntimeTarget | None]:
    snapshot_row = _latest_snapshot_record(db)
    return snapshot_row, _fresh_matching_target(db, snapshot_row)


def _frontend_command_state(db: Session, snapshot_row: SnapshotRecord | None, target: RuntimeTarget | None) -> dict[str, Any]:
    refresh_terminal_states(db)
    commands: list[CommandRecord] = []
    if snapshot_row is not None:
        commands = db.scalars(
            select(CommandRecord)
            .where(CommandRecord.target_session_id == snapshot_row.session_id, CommandRecord.target_run_id == snapshot_row.run_id)
            .order_by(CommandRecord.created_at_utc.desc())
            .limit(30)
        ).all()

    area_policies: dict[str, str] = {area_id: "NORMAL" for area_id in ("A", "B", "C", "D")}
    resolved_areas: set[str] = set()
    for command in commands:
        if command.command_type != "SET_AREA_POLICY" or command.status != "SUCCEEDED":
            continue
        area_id = _normalize_area_id((command.payload or {}).get("areaId"))
        if not area_id or area_id in resolved_areas:
            continue
        policy = str((command.payload or {}).get("policy") or "NORMAL").upper()
        if policy not in {"NORMAL", "PRIORITY", "CLOSED", "RESTRICTED"}:
            policy = "NORMAL"
        area_policies[area_id] = policy
        resolved_areas.add(area_id)

    snapshot_identity = None
    if snapshot_row is not None:
        payload = snapshot_row.payload or {}
        snapshot_identity = {
            "sourceId": snapshot_row.source_system,
            "scene": str(payload.get("sceneName") or ""),
            "sessionId": snapshot_row.session_id,
            "runId": snapshot_row.run_id,
        }

    command_target = None
    if target is not None:
        command_target = {
            "sourceId": target.source_id,
            "sessionId": target.session_id,
            "runId": target.run_id,
        }

    matches = bool(
        snapshot_row is not None
        and target is not None
        and target.session_id == snapshot_row.session_id
        and target.run_id == snapshot_row.run_id
    )
    return {
        "areaPolicies": area_policies,
        "commands": [frontend_command_view(db, command) for command in commands],
        "unityConnected": target is not None,
        "runtimeOnlineSeconds": settings.runtime_online_seconds,
        "commandTarget": command_target,
        "snapshotIdentity": snapshot_identity,
        "commandTargetMatchesSnapshot": matches,
    }
