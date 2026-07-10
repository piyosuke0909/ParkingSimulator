from __future__ import annotations

import hashlib
import json
import os
import re
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import uuid4

from models.schemas import CarSnapshot, ParkingSlotSnapshot, UnitySnapshot, WaypointSnapshot


IMAGE_WIDTH = 637
IMAGE_HEIGHT = 492
WORLD_MIN_X = -180
WORLD_MAX_X = 180
WORLD_MIN_Z = -150
WORLD_MAX_Z = 150
FLIP_X = True
FLIP_Z = False
MAP_X_SCALE = 1.273
MAP_Y_SCALE = 1.364
RESERVATION_TTL = timedelta(minutes=5)
REPLAN_COOLDOWN = timedelta(seconds=30)
STALE_AFTER = timedelta(seconds=5)


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


ROUTE_PATHS = {
    "A": "M28 246 H160 V164 H204",
    "B": "M28 246 H160 V328 H204",
    "C": "M28 246 H360 V164 H434",
    "D": "M28 246 H360 V328 H434",
}


@dataclass
class Reservation:
    reservation_id: str
    user_session_id: str
    target_area_id: str
    target_slot_id: str | None
    assigned_car_id: str | None
    status: str
    created_at: datetime
    expires_at: datetime
    last_replanned_at: datetime | None = None


@dataclass
class StateStore:
    latest_snapshot: UnitySnapshot | None = None
    latest_sequence_by_source_scene: dict[str, int] = field(default_factory=dict)
    snapshot_version: int = 0
    snapshot_received_at: datetime | None = None
    area_layout: dict[str, dict[str, Any]] = field(default_factory=lambda: dict(DEFAULT_AREA_POLYGONS))
    reservations: dict[str, Reservation] = field(default_factory=dict)
    logs: list[dict[str, Any]] = field(default_factory=list)
    ai_cache: dict[str, dict[str, Any]] = field(default_factory=dict)
    last_valid_ai_recommendation: dict[str, Any] | None = None

    def now(self) -> datetime:
        return datetime.now(UTC)

    def ingest_snapshot(self, snapshot: UnitySnapshot) -> dict[str, Any]:
        key = f"{snapshot.sourceId}:{snapshot.scene}"
        latest_sequence = self.latest_sequence_by_source_scene.get(key)
        if latest_sequence is not None and snapshot.sequenceNumber <= latest_sequence:
            if self._should_accept_sequence_reset(snapshot):
                self._log(
                    "unity_snapshot_sequence_reset",
                    "Unity snapshot の sequence reset を検知し、新しい起動として受け入れました。",
                    {
                        "sourceId": snapshot.sourceId,
                        "scene": snapshot.scene,
                        "sequenceNumber": snapshot.sequenceNumber,
                        "previousSequenceNumber": latest_sequence,
                    },
                )
            else:
                return {
                    "accepted": False,
                    "reason": "stale_sequence",
                    "latestSequenceNumber": latest_sequence,
                    "snapshotVersion": self.snapshot_version,
                }

        self.latest_sequence_by_source_scene[key] = snapshot.sequenceNumber
        self.latest_snapshot = snapshot
        self.snapshot_version += 1
        self.snapshot_received_at = self.now()
        self.area_layout = self._build_area_layout(snapshot)
        self._log(
            "unity_snapshot_received",
            "Unity snapshot を受信しました。",
            {"sourceId": snapshot.sourceId, "scene": snapshot.scene, "sequenceNumber": snapshot.sequenceNumber},
        )
        return {
            "accepted": True,
            "snapshotVersion": self.snapshot_version,
            "updatedAt": self.snapshot_received_at.isoformat(),
        }

    def _should_accept_sequence_reset(self, snapshot: UnitySnapshot) -> bool:
        if not self.latest_snapshot or not self.snapshot_received_at:
            return False
        if snapshot.sourceId != self.latest_snapshot.sourceId or snapshot.scene != self.latest_snapshot.scene:
            return False
        if not self._is_stale(self.now()):
            return False
        return snapshot.timestamp > self.latest_snapshot.timestamp

    def parking_status(self) -> dict[str, Any]:
        self._prune_expired_reservations()
        now = self.now()
        stale = self._is_stale(now)
        areas = self._area_statuses(now)
        return {
            "snapshotVersion": self.snapshot_version,
            "updatedAt": self._updated_at(now),
            "stale": stale,
            "displayTimeZone": "Asia/Tokyo",
            "source": self.latest_snapshot.sourceId if self.latest_snapshot else None,
            "areas": areas,
            "slots": self._map_slot_responses(now),
            "areaLayout": self._area_layout_response(),
            "summary": self._summary_from_areas(areas),
        }

    def admin_state(self) -> dict[str, Any]:
        status = self.parking_status()
        alerts = self._alerts(status["areas"], status["stale"])
        return {
            **status,
            "guards": self._available_guards(),
            "alerts": alerts,
            "logs": self.logs[-30:],
            "lastAiRecommendation": self.last_valid_ai_recommendation,
        }

    def recommendation(self, user_session_id: str) -> dict[str, Any]:
        self._prune_expired_reservations()
        now = self.now()
        areas = self._area_statuses(now)
        active = self._active_reservation_for_user(user_session_id, now)

        if active:
            if not active.assigned_car_id:
                assigned_car = self._assign_car_to_guidance(active.target_area_id, active.target_slot_id, now)
                active.assigned_car_id = assigned_car.carId if assigned_car else None
            current = next((area for area in areas if area["areaId"] == active.target_area_id), None)
            if current and current["occupancyRate"] >= 0.85:
                forced = current["occupancyRate"] >= 0.95 or current["effectiveAvailable"] <= 0
                cooldown_ok = not active.last_replanned_at or now - active.last_replanned_at >= REPLAN_COOLDOWN
                if forced or cooldown_ok:
                    new_area = self._select_area(areas, exclude_area_id=active.target_area_id)
                    if new_area and new_area["areaId"] != active.target_area_id:
                        old_label = self._area_label(active.target_area_id)
                        new_slot = self._select_recommended_slot(new_area["areaId"], now, active.reservation_id)
                        active.target_area_id = new_area["areaId"]
                        active.target_slot_id = new_slot.slotId if new_slot else None
                        active.last_replanned_at = now
                        active.expires_at = now + RESERVATION_TTL
                        self._log(
                            "guidance_replanned",
                            f"{old_label}が混雑したため、{new_area['label']}へ案内を更新しました。",
                            {"userSessionId": user_session_id, "targetAreaId": new_area["areaId"]},
                        )
                        return self._guidance_response(
                            new_area,
                            active,
                            f"{old_label}が混雑したため、{new_area['label']}へ案内を更新しました。",
                        )
            return self._guidance_response(current or self._select_area(areas), active, None)

        selected = self._select_area(areas)
        return self._guidance_response(selected, None, None)

    def start_guidance(self, user_session_id: str, target_area_id: str | None = None) -> dict[str, Any]:
        self._prune_expired_reservations()
        areas = self._area_statuses(self.now())
        selected = None
        if target_area_id:
            selected = next((area for area in areas if area["areaId"] == target_area_id), None)
        if not selected:
            selected = self._select_area(areas)
        if not selected:
            raise ValueError("案内可能なエリアがありません。")

        now = self.now()
        selected_slot = self._select_recommended_slot(selected["areaId"], now)
        assigned_car = self._assign_car_to_guidance(selected["areaId"], selected_slot.slotId if selected_slot else None, now)
        reservation = Reservation(
            reservation_id=f"res_{uuid4().hex[:12]}",
            user_session_id=user_session_id,
            target_area_id=selected["areaId"],
            target_slot_id=selected_slot.slotId if selected_slot else None,
            assigned_car_id=assigned_car.carId if assigned_car else None,
            status="active",
            created_at=now,
            expires_at=now + RESERVATION_TTL,
        )
        self.reservations[reservation.reservation_id] = reservation
        self._log(
            "guidance_started",
            f"{selected['label']}への案内予約を開始しました。",
            {
                "reservationId": reservation.reservation_id,
                "userSessionId": user_session_id,
                "targetAreaId": selected["areaId"],
                "targetSlotId": reservation.target_slot_id,
                "assignedCarId": reservation.assigned_car_id,
            },
        )
        return self._guidance_response(selected, reservation, None)

    def cancel_guidance(self, user_session_id: str, reservation_id: str | None = None) -> dict[str, Any]:
        now = self.now()
        cancelled = []
        for reservation in self.reservations.values():
            if reservation.user_session_id != user_session_id:
                continue
            if reservation_id and reservation.reservation_id != reservation_id:
                continue
            if reservation.status == "active" and reservation.expires_at > now:
                reservation.status = "cancelled"
                cancelled.append(reservation.reservation_id)
        if cancelled:
            self._log("guidance_cancelled", "案内予約をキャンセルしました。", {"reservationIds": cancelled})
        return {"cancelledReservationIds": cancelled}

    def generate_ai_recommendation(self, instruction: str) -> dict[str, Any]:
        state = self.admin_state()
        ai_input = {
            "areaStatus": state["areas"],
            "alerts": state["alerts"],
            "availableGuards": self._available_guards(),
            "operatorInstruction": instruction,
        }
        cache_key = hashlib.sha256(json.dumps(ai_input, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest()
        if cache_key in self.ai_cache:
            cached = self.ai_cache[cache_key]
            self._log("admin_viewed_recommendation", "キャッシュ済み AI 提案を表示しました。", {"source": cached["source"]})
            return {**cached, "cached": True}

        self._log("ai_generation_requested", "管理者が AI 提案の生成を開始しました。", {})
        try:
            generated = self._call_gemini(ai_input)
            validated = self._validate_ai_recommendation(generated)
            self._log("ai_generation_success", "Gemini AI 提案を生成しました。", {"source": validated["source"]})
        except Exception as exc:
            validated = self._fallback_ai_recommendation(state, str(exc))
            self._log("ai_generation_failed", "Gemini AI 提案の生成に失敗しました。", {"error": str(exc)[:180]})
            self._log("ai_fallback_used", "fallback 提案を表示しました。", {"source": validated["source"]})

        self.ai_cache[cache_key] = validated
        self.last_valid_ai_recommendation = validated
        return {**validated, "cached": False}

    def _call_gemini(self, ai_input: dict[str, Any]) -> dict[str, Any]:
        api_key = os.getenv("GEMINI_API_KEY")
        if not api_key:
            raise RuntimeError("GEMINI_API_KEY is not set")

        model = os.getenv("GEMINI_MODEL", "gemini-3.5-flash")
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={api_key}"
        prompt = (
            "あなたは駐車場運営の管制支援AIです。"
            "警備員の個人情報は使わず、提案・参考・管理者判断の文脈でJSONだけ返してください。"
            "存在しないareaId/guardIdは使わないでください。"
            "不足時も追加人員の断定ではなく、現有人数で優先配置してください。\n\n"
            f"入力JSON:\n{json.dumps(ai_input, ensure_ascii=False)}\n\n"
            "出力JSON keys: title, summary, riskLevel, actions, guardAssignments, operatorNotes"
        )
        body = json.dumps({"contents": [{"parts": [{"text": prompt}]}]}).encode("utf-8")
        request = urllib.request.Request(url, data=body, headers={"Content-Type": "application/json"}, method="POST")
        try:
            with urllib.request.urlopen(request, timeout=12) as response:
                payload = json.loads(response.read().decode("utf-8"))
        except urllib.error.URLError as exc:
            raise RuntimeError(f"Gemini request failed: {exc}") from exc

        text = payload["candidates"][0]["content"]["parts"][0]["text"]
        return self._parse_json_text(text)

    def _parse_json_text(self, text: str) -> dict[str, Any]:
        stripped = text.strip()
        fenced = re.search(r"```(?:json)?\s*(.*?)```", stripped, re.DOTALL)
        if fenced:
            stripped = fenced.group(1).strip()
        try:
            parsed = json.loads(stripped)
        except json.JSONDecodeError:
            start = stripped.find("{")
            end = stripped.rfind("}")
            if start < 0 or end <= start:
                raise
            parsed = json.loads(stripped[start : end + 1])
        if not isinstance(parsed, dict):
            raise ValueError("Gemini response JSON must be an object")
        return parsed

    def _validate_ai_recommendation(self, data: dict[str, Any]) -> dict[str, Any]:
        known_areas = {area["areaId"] for area in AREA_MASTER}
        known_guards = {guard["guardId"] for guard in self._available_guards()}
        actions = []
        for index, action in enumerate(self._as_list(data.get("actions"))):
            normalized = self._normalize_ai_action(action, known_areas, index)
            if normalized:
                actions.append(normalized)
        assignments = []
        for assignment in self._as_list(data.get("guardAssignments")):
            if not isinstance(assignment, dict):
                continue
            target_area = assignment.get("targetArea") or assignment.get("areaId")
            if assignment.get("guardId") in known_guards and target_area in known_areas:
                assignments.append({**assignment, "targetArea": target_area})
        return {
            "generatedAt": self.now().isoformat(),
            "source": "gemini",
            "title": str(data.get("title") or "駐車場運営の警備配置提案"),
            "summary": str(data.get("summary") or "現在の混雑状況をもとに、管理者判断用の提案を生成しました。"),
            "riskLevel": data.get("riskLevel") if data.get("riskLevel") in {"low", "medium", "high"} else "medium",
            "actions": actions,
            "guardAssignments": assignments,
            "operatorNotes": self._normalize_ai_notes(data.get("operatorNotes")),
        }

    def _as_list(self, value: Any) -> list[Any]:
        if value is None:
            return []
        return value if isinstance(value, list) else [value]

    def _normalize_ai_action(self, action: Any, known_areas: set[str], index: int) -> dict[str, Any] | None:
        if isinstance(action, dict):
            area_id = self._normalize_area_id(str(action.get("areaId") or action.get("targetArea") or ""))
            if area_id not in known_areas:
                return None
            return {
                "priority": int(action.get("priority") or index + 1),
                "areaId": area_id,
                "action": str(action.get("action") or action.get("title") or action.get("summary") or ""),
                "reason": str(action.get("reason") or ""),
            }

        text = str(action).strip()
        if not text:
            return None
        matched_area = next((area_id for area_id in sorted(known_areas) if re.search(rf"\b{area_id}\b|{area_id}エリア", text)), None)
        if not matched_area:
            return None
        return {"priority": index + 1, "areaId": matched_area, "action": text, "reason": "Gemini output normalized from text"}

    def _normalize_ai_notes(self, value: Any) -> list[str]:
        notes = []
        for note in self._as_list(value):
            if isinstance(note, dict):
                notes.append(json.dumps(note, ensure_ascii=False))
            else:
                notes.append(str(note))
        return notes[:5]

    def _fallback_ai_recommendation(self, state: dict[str, Any], error: str) -> dict[str, Any]:
        areas = sorted(state["areas"], key=lambda area: area["riskScore"], reverse=True)
        high = areas[0] if areas else {"label": "対象エリア", "areaId": "A", "riskLevel": "medium"}
        guards = self._available_guards()
        assignments = []
        for guard, area in zip(guards, areas):
            assignments.append(
                {
                    "guardId": guard["guardId"],
                    "targetArea": area["areaId"],
                    "task": f"{area['label']}周辺で分散案内を行う",
                    "durationMinutes": 20,
                    "reason": "現有警備員でリスクの高いエリアを優先するため",
                }
            )
        return {
            "generatedAt": self.now().isoformat(),
            "source": "fallback",
            "title": "AI生成に失敗したため、ルールベース提案を表示",
            "summary": f"{high['label']}のリスクが高いため、現有{len(guards)}名で優先配置してください。",
            "riskLevel": high.get("riskLevel", "medium"),
            "actions": [
                {
                    "priority": 1,
                    "areaId": high["areaId"],
                    "action": f"{high['label']}への車両集中を避け、空きのあるエリアへ分散案内する",
                    "reason": "混雑率、待機車両、出庫車両を加味した risk score が高いため",
                }
            ],
            "guardAssignments": assignments,
            "operatorNotes": ["提案は参考です。最終配置は管理者が判断してください。", f"fallback reason: {error[:120]}"],
        }

    def _area_statuses(self, now: datetime) -> list[dict[str, Any]]:
        snapshot = self.latest_snapshot
        metrics = self._metrics_from_snapshot(snapshot)
        active_counts = self._active_reservation_counts(now)
        statuses = []
        for master in AREA_MASTER:
            area_id = master["areaId"]
            raw = metrics.get(area_id, self._default_metric(area_id))
            capacity = max(raw["capacity"], 1)
            occupied_count = min(raw["occupiedCount"] + raw["reservedCount"], capacity)
            empty_count = max(capacity - occupied_count - raw["disabledCount"], 0)
            occupancy_rate = occupied_count / capacity
            effective_available = max(empty_count - active_counts.get(area_id, 0), 0)
            risk_score = (
                occupancy_rate * 100
                + raw["waitingCars"] * 10
                + raw["leavingCars"] * 5
                + raw["staleVehicles"] * 15
            )
            risk_level = "high" if risk_score >= 90 else "medium" if risk_score >= 70 else "low"
            statuses.append(
                {
                    **master,
                    "capacity": capacity,
                    "emptyCount": empty_count,
                    "occupiedCount": occupied_count,
                    "reservedCount": raw["reservedCount"],
                    "disabledCount": raw["disabledCount"],
                    "waitingCars": raw["waitingCars"],
                    "leavingCars": raw["leavingCars"],
                    "staleVehicles": raw["staleVehicles"],
                    "activeAreaReservations": active_counts.get(area_id, 0),
                    "effectiveAvailable": effective_available,
                    "occupancyRate": round(occupancy_rate, 4),
                    "riskScore": round(risk_score, 2),
                    "riskLevel": risk_level,
                    "layout": self.area_layout.get(area_id, DEFAULT_AREA_POLYGONS[area_id]),
                }
            )
        return statuses

    def _metrics_from_snapshot(self, snapshot: UnitySnapshot | None) -> dict[str, dict[str, int]]:
        metrics = {area["areaId"]: self._empty_metric() for area in AREA_MASTER}
        if not snapshot:
            return {area_id: self._default_metric(area_id) for area_id in metrics}
        if not snapshot.slots:
            return self._metrics_from_summary(snapshot)

        slot_to_area = {}
        for slot in snapshot.slots:
            area_id = self._normalize_area_id(slot.areaId)
            if area_id not in metrics:
                continue
            slot_to_area[slot.slotId] = area_id
            metric = metrics[area_id]
            metric["capacity"] += 1
            state = slot.state.lower()
            if "disabled" in state or "closed" in state:
                metric["disabledCount"] += 1
            elif slot.sensorOccupied or "occupied" in state or "parking" in state:
                metric["occupiedCount"] += 1
            elif "reserved" in state:
                metric["reservedCount"] += 1
            if slot.isLeaving or "leaving" in state:
                metric["leavingCars"] += 1

        for car in snapshot.cars:
            area_id = self._normalize_area_id(car.targetAreaId or "")
            if area_id not in metrics and car.targetSlotId:
                area_id = slot_to_area.get(car.targetSlotId, "")
            if area_id not in metrics:
                continue
            state = car.state.lower()
            if "waiting" in state or "queue" in state:
                metrics[area_id]["waitingCars"] += 1
            if car.isStoppedByFrontCar or "stalled" in state:
                metrics[area_id]["staleVehicles"] += 1
        return metrics

    def _metrics_from_summary(self, snapshot: UnitySnapshot) -> dict[str, dict[str, int]]:
        total = snapshot.summary.empty + snapshot.summary.reserved + snapshot.summary.occupied + snapshot.summary.disabled
        total = total if total > 0 else 240
        per_area_capacity = max(total // len(AREA_MASTER), 1)
        metrics = {}
        for index, area in enumerate(AREA_MASTER):
            ratio = [0.72, 0.54, 0.48, 0.63][index]
            occupied = int(per_area_capacity * ratio)
            metrics[area["areaId"]] = {
                "capacity": per_area_capacity,
                "occupiedCount": occupied,
                "reservedCount": 0,
                "disabledCount": 0,
                "waitingCars": 1 if ratio > 0.7 else 0,
                "leavingCars": 1 if ratio > 0.6 else 0,
                "staleVehicles": 0,
            }
        return metrics

    def _default_metric(self, area_id: str) -> dict[str, int]:
        defaults = {
            "A": {"capacity": 60, "occupiedCount": 48, "reservedCount": 0, "disabledCount": 0, "waitingCars": 2, "leavingCars": 1, "staleVehicles": 0},
            "B": {"capacity": 60, "occupiedCount": 32, "reservedCount": 0, "disabledCount": 0, "waitingCars": 0, "leavingCars": 1, "staleVehicles": 0},
            "C": {"capacity": 60, "occupiedCount": 26, "reservedCount": 0, "disabledCount": 0, "waitingCars": 0, "leavingCars": 0, "staleVehicles": 0},
            "D": {"capacity": 60, "occupiedCount": 40, "reservedCount": 0, "disabledCount": 0, "waitingCars": 1, "leavingCars": 1, "staleVehicles": 0},
        }
        return defaults[area_id].copy()

    def _empty_metric(self) -> dict[str, int]:
        return {"capacity": 0, "occupiedCount": 0, "reservedCount": 0, "disabledCount": 0, "waitingCars": 0, "leavingCars": 0, "staleVehicles": 0}

    def _build_area_layout(self, snapshot: UnitySnapshot) -> dict[str, dict[str, Any]]:
        slot_counts: dict[str, int] = {area["areaId"]: 0 for area in AREA_MASTER}
        for slot in snapshot.slots:
            area_id = self._normalize_area_id(slot.areaId)
            if area_id in slot_counts:
                slot_counts[area_id] += 1

        layout = {}
        for area in AREA_MASTER:
            area_id = area["areaId"]
            layout[area_id] = {**DEFAULT_AREA_POLYGONS[area_id], "slotCount": slot_counts[area_id]}
        return layout

    def _world_to_map(self, x: float, z: float) -> dict[str, float]:
        norm_x = (x - WORLD_MIN_X) / (WORLD_MAX_X - WORLD_MIN_X)
        norm_z = (z - WORLD_MIN_Z) / (WORLD_MAX_Z - WORLD_MIN_Z)
        if FLIP_X:
            norm_x = 1 - norm_x
        if FLIP_Z:
            norm_z = 1 - norm_z
        map_x = norm_x * IMAGE_WIDTH
        map_y = norm_z * IMAGE_HEIGHT
        map_x = IMAGE_WIDTH / 2 + (map_x - IMAGE_WIDTH / 2) * MAP_X_SCALE
        map_y = IMAGE_HEIGHT / 2 + (map_y - IMAGE_HEIGHT / 2) * MAP_Y_SCALE
        return {"x": max(0, min(IMAGE_WIDTH, map_x)), "y": max(0, min(IMAGE_HEIGHT, map_y))}

    def _slot_by_id(self, slot_id: str | None) -> ParkingSlotSnapshot | None:
        if not slot_id or not self.latest_snapshot:
            return None
        return next((slot for slot in self.latest_snapshot.slots if slot.slotId == slot_id), None)

    def _is_slot_available(self, slot: ParkingSlotSnapshot) -> bool:
        state = slot.state.lower()
        if "disabled" in state or "closed" in state:
            return False
        if slot.sensorOccupied or slot.isLeaving:
            return False
        if slot.reservedByCarId or slot.occupiedByCarId:
            return False
        if "occupied" in state or "reserved" in state or "leaving" in state or "parking" in state:
            return False
        return True

    def _active_reserved_slot_ids(self, now: datetime, exclude_reservation_id: str | None = None) -> set[str]:
        reserved: set[str] = set()
        for reservation in self.reservations.values():
            if reservation.reservation_id == exclude_reservation_id:
                continue
            if reservation.status == "active" and reservation.expires_at > now and reservation.target_slot_id:
                reserved.add(reservation.target_slot_id)
        return reserved

    def _available_slots_for_area(
        self,
        area_id: str,
        now: datetime,
        exclude_reservation_id: str | None = None,
    ) -> list[ParkingSlotSnapshot]:
        if not self.latest_snapshot or not self.latest_snapshot.slots:
            return []
        reserved_slot_ids = self._active_reserved_slot_ids(now, exclude_reservation_id)
        normalized_area_id = self._normalize_area_id(area_id)
        return [
            slot
            for slot in self.latest_snapshot.slots
            if self._normalize_area_id(slot.areaId) == normalized_area_id
            and slot.slotId not in reserved_slot_ids
            and self._is_slot_available(slot)
        ]

    def _slot_sort_key(self, slot: ParkingSlotSnapshot) -> tuple[int, str]:
        match = re.search(r"(\d+)$", slot.slotId)
        return (int(match.group(1)) if match else 9999, slot.slotId)

    def _select_recommended_slot(
        self,
        area_id: str,
        now: datetime,
        exclude_reservation_id: str | None = None,
    ) -> ParkingSlotSnapshot | None:
        slots = self._available_slots_for_area(area_id, now, exclude_reservation_id)
        if not slots:
            return None
        with_waypoint = [slot for slot in slots if slot.accessWaypointId]
        return sorted(with_waypoint or slots, key=self._slot_sort_key)[0]

    def _slot_response(self, slot: ParkingSlotSnapshot | None) -> dict[str, Any] | None:
        if not slot:
            return None
        position = slot.parkingPoint or slot.position
        map_position = self._world_to_map(position.x, position.z) if position else None
        return {
            "slotId": slot.slotId,
            "areaId": self._normalize_area_id(slot.areaId),
            "label": slot.slotId,
            "state": slot.state,
            "position": None if not position else {"x": position.x, "y": position.y, "z": position.z},
            "mapPosition": map_position,
            "accessWaypointId": slot.accessWaypointId,
        }

    def _car_by_id(self, car_id: str | None) -> CarSnapshot | None:
        if not car_id or not self.latest_snapshot:
            return None
        return next((car for car in self.latest_snapshot.cars if car.carId == car_id), None)

    def _active_assigned_car_ids(self, now: datetime) -> set[str]:
        assigned = set()
        for reservation in self.reservations.values():
            if reservation.status == "active" and reservation.expires_at > now and reservation.assigned_car_id:
                assigned.add(reservation.assigned_car_id)
        return assigned

    def _is_assignable_car(self, car: CarSnapshot) -> bool:
        if not car.carId or not car.position:
            return False
        state = car.state.lower()
        blocked = ("parked", "parking", "leaving", "backing", "backout", "finished", "exited", "disabled")
        return not any(token in state for token in blocked)

    def _target_area_for_car(self, car: CarSnapshot) -> str:
        area_id = self._normalize_area_id(car.targetAreaId or "")
        if area_id:
            return area_id
        slot = self._slot_by_id(car.targetSlotId)
        return self._normalize_area_id(slot.areaId) if slot else ""

    def _distance_to_nearest_entrance(self, car: CarSnapshot) -> float:
        if not car.position or not self.latest_snapshot:
            return 0
        entrances = [waypoint for waypoint in self.latest_snapshot.waypoints if waypoint.isEntrance]
        if not entrances:
            return 0
        return min(
            (car.position.x - waypoint.position.x) ** 2 + (car.position.z - waypoint.position.z) ** 2
            for waypoint in entrances
        )

    def _car_assignment_score(self, car: CarSnapshot, target_area_id: str, target_slot_id: str | None) -> tuple[int, float, str]:
        car_area_id = self._target_area_for_car(car)
        if target_slot_id and car.targetSlotId == target_slot_id:
            match_score = 0
        elif car_area_id == target_area_id:
            match_score = 10
        elif not car.targetSlotId and not car.targetAreaId:
            match_score = 20
        else:
            match_score = 80
        return (match_score, self._distance_to_nearest_entrance(car), car.carId)

    def _assign_car_to_guidance(self, target_area_id: str, target_slot_id: str | None, now: datetime) -> CarSnapshot | None:
        if not self.latest_snapshot or not self.latest_snapshot.cars:
            return None
        assigned_car_ids = self._active_assigned_car_ids(now)
        normalized_area_id = self._normalize_area_id(target_area_id)
        candidates = [
            car
            for car in self.latest_snapshot.cars
            if car.carId not in assigned_car_ids and self._is_assignable_car(car)
            and self._target_area_for_car(car) in {"", normalized_area_id}
        ]
        if not candidates:
            return None
        return sorted(candidates, key=lambda car: self._car_assignment_score(car, normalized_area_id, target_slot_id))[0]

    def _car_response(self, car_id: str | None) -> dict[str, Any] | None:
        car = self._car_by_id(car_id)
        if not car or not car.position:
            return None
        return {
            "carId": car.carId,
            "state": car.state,
            "position": {"x": car.position.x, "y": car.position.y, "z": car.position.z},
            "mapPosition": self._world_to_map(car.position.x, car.position.z),
            "targetSlotId": car.targetSlotId,
            "targetAreaId": self._target_area_for_car(car) or None,
            "isStoppedByFrontCar": car.isStoppedByFrontCar,
        }

    def _map_slot_responses(self, now: datetime) -> list[dict[str, Any]]:
        if not self.latest_snapshot or not self.latest_snapshot.slots:
            return []

        active_reserved_slot_ids = self._active_reserved_slot_ids(now)
        responses = []
        for slot in self.latest_snapshot.slots:
            position = slot.parkingPoint or slot.position
            if not position:
                continue

            state = slot.state
            if slot.slotId in active_reserved_slot_ids and self._is_slot_available(slot):
                state = "Reserved"

            responses.append(
                {
                    "slotId": slot.slotId,
                    "areaId": self._normalize_area_id(slot.areaId),
                    "state": state,
                    "sensorOccupied": slot.sensorOccupied,
                    "isLeaving": slot.isLeaving,
                    "mapPosition": self._world_to_map(position.x, position.z),
                    "accessWaypointId": slot.accessWaypointId,
                }
            )

        return sorted(responses, key=lambda item: (item["areaId"], item["slotId"]))

    def _waypoint_map(self) -> dict[str, WaypointSnapshot]:
        if not self.latest_snapshot:
            return {}
        return {waypoint.waypointId: waypoint for waypoint in self.latest_snapshot.waypoints if waypoint.waypointId}

    def _find_waypoint_route(self, start_id: str, goal_id: str, waypoints: dict[str, WaypointSnapshot]) -> list[str]:
        if start_id not in waypoints or goal_id not in waypoints:
            return []
        queue = [start_id]
        previous: dict[str, str | None] = {start_id: None}

        while queue:
            current_id = queue.pop(0)
            if current_id == goal_id:
                break

            for next_id in waypoints[current_id].nextWaypointIds:
                if next_id not in waypoints or next_id in previous:
                    continue
                previous[next_id] = current_id
                queue.append(next_id)

        if goal_id not in previous:
            return []

        route = []
        current: str | None = goal_id
        while current is not None:
            route.append(current)
            current = previous[current]
        route.reverse()
        return route

    def _best_waypoint_route_to_slot(self, slot: ParkingSlotSnapshot | None) -> list[str]:
        if not slot or not slot.accessWaypointId:
            return []
        waypoints = self._waypoint_map()
        if not waypoints:
            return []

        entrances = sorted(
            [waypoint.waypointId for waypoint in waypoints.values() if waypoint.isEntrance],
            key=str,
        )
        if not entrances:
            entrances = sorted(waypoints.keys())[:1]

        routes = [
            route
            for route in (self._find_waypoint_route(entrance_id, slot.accessWaypointId, waypoints) for entrance_id in entrances)
            if route
        ]
        if not routes:
            return []
        return sorted(routes, key=len)[0]

    def _route_response(self, area: dict[str, Any], slot: ParkingSlotSnapshot | None, target_label: str) -> dict[str, Any]:
        waypoint_ids = self._best_waypoint_route_to_slot(slot)
        waypoints = self._waypoint_map()
        points: list[dict[str, float]] = []

        for waypoint_id in waypoint_ids:
            waypoint = waypoints.get(waypoint_id)
            if not waypoint:
                continue
            points.append(self._world_to_map(waypoint.position.x, waypoint.position.z))

        slot_position = slot.parkingPoint or slot.position if slot else None
        if slot_position:
            points.append(self._world_to_map(slot_position.x, slot_position.z))

        if len(points) >= 2:
            svg_path = " ".join(
                f"{'M' if index == 0 else 'L'}{round(point['x'])} {round(point['y'])}"
                for index, point in enumerate(points)
            )
            return {
                "svgPath": svg_path,
                "source": "unity-waypoints",
                "waypointIds": waypoint_ids,
                "steps": [
                    "Unity の入口 waypoint から通路に沿って進む",
                    f"{area['label']}方面の access waypoint へ向かう",
                    f"{target_label}付近では徐行し、案内先を確認する",
                ],
            }

        return {
            "svgPath": ROUTE_PATHS.get(area["areaId"], "M28 246 H330 246"),
            "source": "area-fallback",
            "waypointIds": [],
            "steps": [
                "入口から中央通路へ進む",
                f"{area['label']}方面へ向かう",
                f"{target_label}付近では徐行し、案内先を確認する",
            ],
        }

    def _guidance_response(self, area: dict[str, Any] | None, reservation: Reservation | None, replan_reason: str | None) -> dict[str, Any]:
        if not area:
            return {"guidanceLevel": "area", "status": "unavailable", "message": "案内可能なエリアがありません。"}
        now = self.now()
        slot = self._slot_by_id(reservation.target_slot_id) if reservation and reservation.target_slot_id else None
        if slot and not self._is_slot_available(slot):
            slot = None
        if not slot:
            slot = self._select_recommended_slot(area["areaId"], now, reservation.reservation_id if reservation else None)
            if reservation and slot:
                reservation.target_slot_id = slot.slotId
        slot_data = self._slot_response(slot)
        empty = area.get("effectiveAvailable", area.get("emptyCount", 0))
        target_label = slot_data["label"] if slot_data else area["label"]
        message = f"{target_label}へ進み、案内に沿って駐車してください。"
        route = self._route_response(area, slot, target_label)
        assigned_car = self._car_response(reservation.assigned_car_id) if reservation else None
        return {
            "guidanceLevel": "slot" if slot_data else "area",
            "status": "active" if reservation else "not_started",
            "updatedAt": self.now().isoformat(),
            "targetArea": {
                "areaId": area["areaId"],
                "label": area["label"],
                "reason": area.get("reason") or f"{area['label']}は有効空き {empty} 台で、現在の推薦先です。",
            },
            "recommendedSlotId": slot_data["slotId"] if slot_data else None,
            "recommendedSlot": slot_data,
            "message": message,
            "replanReason": replan_reason,
            "reservation": None
            if not reservation
            else {
                "reservationId": reservation.reservation_id,
                "status": reservation.status,
                "targetAreaId": reservation.target_area_id,
                "targetSlotId": reservation.target_slot_id,
                "assignedCarId": reservation.assigned_car_id,
                "expiresAt": reservation.expires_at.isoformat(),
            },
            "assignedCar": assigned_car,
            "route": route,
            "summary": {
                "emptyCount": area["emptyCount"],
                "effectiveAvailable": area["effectiveAvailable"],
                "congestionLevel": area["riskLevel"],
            },
        }

    def _select_area(self, areas: list[dict[str, Any]], exclude_area_id: str | None = None) -> dict[str, Any] | None:
        candidates = [area for area in areas if area["areaId"] != exclude_area_id and area["effectiveAvailable"] > 0]
        if not candidates:
            candidates = [area for area in areas if area["areaId"] != exclude_area_id]
        if not candidates:
            return None
        return sorted(candidates, key=lambda area: (-area["effectiveAvailable"], area["riskScore"], area["displayOrder"]))[0]

    def _active_reservation_counts(self, now: datetime) -> dict[str, int]:
        counts: dict[str, int] = {}
        for reservation in self.reservations.values():
            if reservation.status == "active" and reservation.expires_at > now:
                counts[reservation.target_area_id] = counts.get(reservation.target_area_id, 0) + 1
        return counts

    def _active_reservation_for_user(self, user_session_id: str, now: datetime) -> Reservation | None:
        candidates = [
            reservation
            for reservation in self.reservations.values()
            if reservation.user_session_id == user_session_id and reservation.status == "active" and reservation.expires_at > now
        ]
        return sorted(candidates, key=lambda reservation: reservation.created_at, reverse=True)[0] if candidates else None

    def _prune_expired_reservations(self) -> None:
        now = self.now()
        for reservation in self.reservations.values():
            if reservation.status == "active" and reservation.expires_at <= now:
                reservation.status = "expired"

    def _alerts(self, areas: list[dict[str, Any]], stale: bool) -> list[dict[str, Any]]:
        alerts = []
        if stale:
            alerts.append({"type": "stale_snapshot", "areaId": None, "severity": "high", "message": "Unity snapshot が5秒以上更新されていません。"})
        for area in areas:
            if area["occupancyRate"] >= 0.95:
                alerts.append({"type": "congestion", "areaId": area["areaId"], "severity": "high", "message": f"{area['label']}が満車に近い状態です。"})
            elif area["occupancyRate"] >= 0.85:
                alerts.append({"type": "congestion", "areaId": area["areaId"], "severity": "medium", "message": f"{area['label']}が混雑しています。"})
        return alerts

    def _available_guards(self) -> list[dict[str, Any]]:
        return [
            {"guardId": "G01", "status": "active", "currentArea": "A", "shift": "09:00-18:00", "break": "13:00-14:00", "canMove": True},
            {"guardId": "G02", "status": "active", "currentArea": "C", "shift": "10:00-19:00", "break": "14:00-15:00", "canMove": True},
        ]

    def _area_layout_response(self) -> dict[str, Any]:
        return {"generatedFrom": "unity-slots" if self.latest_snapshot and self.latest_snapshot.slots else "default", "image": {"width": IMAGE_WIDTH, "height": IMAGE_HEIGHT}, "areas": self.area_layout}

    def _summary_from_areas(self, areas: list[dict[str, Any]]) -> dict[str, int | float | str]:
        capacity = sum(area["capacity"] for area in areas)
        empty = sum(area["emptyCount"] for area in areas)
        occupied = sum(area["occupiedCount"] for area in areas)
        occupancy_rate = occupied / capacity if capacity else 0
        return {"capacity": capacity, "emptyCount": empty, "occupiedCount": occupied, "occupancyRate": round(occupancy_rate, 4)}

    def _normalize_area_id(self, area_id: str) -> str:
        value = (area_id or "").strip().upper()
        return value[:1] if value[:1] in {"A", "B", "C", "D"} else value

    def _area_label(self, area_id: str) -> str:
        return next((area["label"] for area in AREA_MASTER if area["areaId"] == area_id), f"{area_id}エリア")

    def _is_stale(self, now: datetime) -> bool:
        return not self.snapshot_received_at or now - self.snapshot_received_at >= STALE_AFTER

    def _updated_at(self, now: datetime) -> str:
        return (self.snapshot_received_at or now).isoformat()

    def _log(self, event_type: str, message: str, metadata: dict[str, Any]) -> None:
        self.logs.append(
            {
                "id": f"log_{uuid4().hex[:10]}",
                "timestamp": self.now().isoformat(),
                "type": event_type,
                "message": message,
                "metadata": metadata,
            }
        )
        if len(self.logs) > 300:
            self.logs = self.logs[-300:]


store = StateStore()
