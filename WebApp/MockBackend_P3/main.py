from __future__ import annotations

import hashlib
import json
import os
import threading
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional

from fastapi import FastAPI, Header, HTTPException, Query, Request
from fastapi.responses import HTMLResponse, JSONResponse, Response
from jsonschema import Draft202012Validator, FormatChecker

BASE_DIR = Path(__file__).resolve().parent
DATA_DIR = BASE_DIR / "data"
SNAPSHOT_DIR = DATA_DIR / "snapshots"
STATE_FILE = DATA_DIR / "mock_state.json"
EVENT_FILE = DATA_DIR / "received_events.jsonl"
LATEST_SNAPSHOT_FILE = DATA_DIR / "snapshot_latest.json"
SCHEMA_DIR = BASE_DIR / "schemas"

API_KEY = os.getenv("SMARTPARKING_LOCAL_API_KEY", "local-dev-key")
MAX_EVENT_BYTES = 1024 * 1024
MAX_SNAPSHOT_BYTES = 5 * 1024 * 1024
MAX_EVENT_COUNT = 50
MAX_COMMAND_COUNT = 10
COMMAND_LEASE_SECONDS = 10
COMMAND_MAX_DELIVERIES = 3
TERMINAL_EVENT_TYPES = {
    "command.succeeded",
    "command.failed",
    "command.rejected",
    "command.expired",
}

app = FastAPI(title="SmartParking P3 Mock Backend", version="1.0-mock")
_lock = threading.RLock()


def now_utc() -> datetime:
    return datetime.now(timezone.utc)


def iso_z(value: Optional[datetime] = None) -> str:
    value = value or now_utc()
    return value.astimezone(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def parse_dt(value: str) -> datetime:
    text = value.strip().replace("Z", "+00:00")
    parsed = datetime.fromisoformat(text)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def canonical_hash(obj: Any) -> str:
    raw = json.dumps(obj, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def safe_file_name(value: str) -> str:
    return "".join(c if c.isalnum() or c in "-_." else "_" for c in value)[:180] or "unnamed"


def load_json(path: Path) -> Dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


EVENT_SCHEMA = load_json(SCHEMA_DIR / "P2_Event_Contract_v1.schema.json")
SNAPSHOT_SCHEMA = load_json(SCHEMA_DIR / "P2_Snapshot_Contract_v1_1.schema.json")
COMMAND_SCHEMA = load_json(SCHEMA_DIR / "P3_Command_Contract_v1.schema.json")
EVENT_VALIDATOR = Draft202012Validator(EVENT_SCHEMA, format_checker=FormatChecker())
SNAPSHOT_VALIDATOR = Draft202012Validator(SNAPSHOT_SCHEMA, format_checker=FormatChecker())
COMMAND_BATCH_VALIDATOR = Draft202012Validator(COMMAND_SCHEMA, format_checker=FormatChecker())


def default_state() -> Dict[str, Any]:
    return {
        "activeTarget": None,
        "commands": [],
        "snapshotHashes": {},
        "eventHashes": {},
        "lastEventAtUtc": None,
        "lastSnapshotAtUtc": None,
    }


def load_state() -> Dict[str, Any]:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    SNAPSHOT_DIR.mkdir(parents=True, exist_ok=True)
    if not STATE_FILE.exists():
        return default_state()
    try:
        state = json.loads(STATE_FILE.read_text(encoding="utf-8"))
        base = default_state()
        base.update(state)
        return base
    except Exception:
        return default_state()


STATE = load_state()


def save_state() -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    tmp = STATE_FILE.with_suffix(".tmp")
    tmp.write_text(json.dumps(STATE, ensure_ascii=False, indent=2), encoding="utf-8", newline="\n")
    tmp.replace(STATE_FILE)


def require_api_key(x_api_key: Optional[str]) -> None:
    if not x_api_key or x_api_key != API_KEY:
        raise HTTPException(status_code=401, detail={"error": {"code": "INVALID_API_KEY", "message": "X-API-Key is missing or invalid."}})


def schema_errors(validator: Draft202012Validator, obj: Any) -> List[str]:
    errors = sorted(validator.iter_errors(obj), key=lambda e: list(e.absolute_path))
    out: List[str] = []
    for error in errors[:12]:
        path = ".".join(str(p) for p in error.absolute_path) or "$"
        out.append(f"{path}: {error.message}")
    return out


def error_response(status: int, code: str, message: str, details: Optional[List[str]] = None) -> JSONResponse:
    body: Dict[str, Any] = {"error": {"code": code, "message": message}}
    if details:
        body["error"]["details"] = details
    return JSONResponse(status_code=status, content=body)


def command_public(record: Dict[str, Any]) -> Dict[str, Any]:
    return record["command"]


def refresh_command_timeouts() -> None:
    current = now_utc()
    changed = False
    for record in STATE["commands"]:
        if record.get("terminalState"):
            continue
        command = record["command"]
        try:
            expires = parse_dt(command["expiresAtUtc"])
        except Exception:
            expires = current
        if current >= expires:
            record["terminalState"] = "EXPIRED"
            record["terminalAtUtc"] = iso_z(current)
            changed = True
            continue
        deliveries = int(record.get("deliveryCount", 0))
        last_delivery = record.get("lastDeliveredAtUtc")
        if deliveries >= COMMAND_MAX_DELIVERIES and last_delivery:
            try:
                if current >= parse_dt(last_delivery) + timedelta(seconds=COMMAND_LEASE_SECONDS):
                    record["terminalState"] = "TIMED_OUT"
                    record["terminalAtUtc"] = iso_z(current)
                    changed = True
            except Exception:
                pass
    if changed:
        save_state()


def active_target_or_409(source_id: str, session_id: str, run_id: str) -> None:
    target = STATE.get("activeTarget")
    current = {"sourceId": source_id, "sessionId": session_id, "runId": run_id}
    if target is None:
        STATE["activeTarget"] = {**current, "lastSeenUtc": iso_z()}
        save_state()
        return

    same = all(target.get(k) == current[k] for k in ("sourceId", "sessionId", "runId"))
    if same:
        target["lastSeenUtc"] = iso_z()
        save_state()
        return

    # Mock convenience: automatically switch to a new Unity run only when there are no
    # non-terminal Commands for the old target. This keeps normal restart/run changes easy.
    outstanding = [r for r in STATE["commands"] if not r.get("terminalState")]
    if not outstanding:
        STATE["activeTarget"] = {**current, "lastSeenUtc": iso_z()}
        save_state()
        return

    raise HTTPException(status_code=409, detail={"error": {"code": "SESSION_NOT_ACTIVE", "message": "Another Unity target still has unfinished mock Commands. Reset the mock target or finish them first."}})


@app.get("/api/health")
def health(x_api_key: Optional[str] = Header(default=None, alias="X-API-Key")):
    require_api_key(x_api_key)
    return {"status": "ok", "service": "smartparking-p3-mock", "serverTimeUtc": iso_z()}


@app.get("/api/v1/commands")
def get_commands(
    sourceId: str = Query(..., min_length=1),
    sessionId: str = Query(..., min_length=1),
    runId: str = Query(..., min_length=1),
    limit: int = Query(10, ge=1, le=10),
    x_api_key: Optional[str] = Header(default=None, alias="X-API-Key"),
):
    require_api_key(x_api_key)
    with _lock:
        refresh_command_timeouts()
        active_target_or_409(sourceId, sessionId, runId)
        current = now_utc()
        eligible: List[Dict[str, Any]] = []
        for record in STATE["commands"]:
            if record.get("terminalState"):
                continue
            command = record["command"]
            if command.get("targetSourceId") != sourceId or command.get("targetSessionId") != sessionId or command.get("targetRunId") != runId:
                continue
            try:
                if current >= parse_dt(command["expiresAtUtc"]):
                    continue
            except Exception:
                continue
            deliveries = int(record.get("deliveryCount", 0))
            if deliveries >= COMMAND_MAX_DELIVERIES:
                continue
            last_delivery = record.get("lastDeliveredAtUtc")
            if deliveries > 0 and last_delivery:
                try:
                    if current < parse_dt(last_delivery) + timedelta(seconds=COMMAND_LEASE_SECONDS):
                        continue
                except Exception:
                    continue
            eligible.append(record)

        eligible.sort(key=lambda r: (-int(r["command"].get("priority", 0)), r["command"].get("createdAtUtc", ""), r["command"].get("commandId", "")))
        selected = eligible[:limit]
        for record in selected:
            record["deliveryCount"] = int(record.get("deliveryCount", 0)) + 1
            record["lastDeliveredAtUtc"] = iso_z(current)
        if selected:
            save_state()

        batch = {
            "contractName": "smart-parking.command-batch",
            "schemaVersion": "1.0",
            "serverTimeUtc": iso_z(current),
            "leaseSeconds": COMMAND_LEASE_SECONDS,
            "commands": [command_public(r) for r in selected],
        }
        errs = schema_errors(COMMAND_BATCH_VALIDATOR, batch)
        if errs:
            return error_response(500, "MOCK_COMMAND_SCHEMA_ERROR", "Mock generated an invalid Command batch.", errs)
        return batch


@app.post("/api/v1/snapshots")
async def post_snapshot(request: Request, x_api_key: Optional[str] = Header(default=None, alias="X-API-Key")):
    require_api_key(x_api_key)
    if request.headers.get("content-type", "").split(";", 1)[0].strip().lower() != "application/json":
        return error_response(415, "UNSUPPORTED_MEDIA_TYPE", "Snapshot Content-Type must be application/json.")
    raw = await request.body()
    if len(raw) > MAX_SNAPSHOT_BYTES:
        return error_response(413, "PAYLOAD_TOO_LARGE", "Snapshot request exceeds 5 MiB.")
    try:
        obj = json.loads(raw.decode("utf-8"))
    except Exception as exc:
        return error_response(400, "INVALID_JSON", f"Snapshot JSON is malformed: {exc}")
    errs = schema_errors(SNAPSHOT_VALIDATOR, obj)
    if errs:
        return error_response(422, "SCHEMA_VALIDATION_FAILED", "Snapshot v1.1 validation failed.", errs)
    snapshot_id = obj["snapshotId"]
    payload_hash = canonical_hash(obj)
    with _lock:
        previous = STATE["snapshotHashes"].get(snapshot_id)
        if previous and previous != payload_hash:
            return error_response(409, "ID_PAYLOAD_CONFLICT", "snapshotId was reused with different content.")
        if not previous:
            STATE["snapshotHashes"][snapshot_id] = payload_hash
            STATE["lastSnapshotAtUtc"] = iso_z()
            text = json.dumps(obj, ensure_ascii=False, indent=2)
            (SNAPSHOT_DIR / f"{safe_file_name(snapshot_id)}.json").write_text(text, encoding="utf-8", newline="\n")
            LATEST_SNAPSHOT_FILE.write_text(text, encoding="utf-8", newline="\n")
            save_state()
    return Response(status_code=204)


@app.post("/api/v1/events")
async def post_events(request: Request, x_api_key: Optional[str] = Header(default=None, alias="X-API-Key")):
    require_api_key(x_api_key)
    if request.headers.get("content-type", "").split(";", 1)[0].strip().lower() != "application/x-ndjson":
        return error_response(415, "UNSUPPORTED_MEDIA_TYPE", "Event Content-Type must be application/x-ndjson.")
    raw = await request.body()
    if len(raw) > MAX_EVENT_BYTES:
        return error_response(413, "PAYLOAD_TOO_LARGE", "Event request exceeds 1 MiB.")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as exc:
        return error_response(400, "INVALID_UTF8", f"Event body is not UTF-8: {exc}")
    lines = [line for line in text.splitlines() if line.strip()]
    if not lines:
        return error_response(400, "EMPTY_NDJSON", "Event NDJSON body is empty.")
    if len(lines) > MAX_EVENT_COUNT:
        return error_response(422, "TOO_MANY_EVENTS", "Event batch exceeds 50 records.")
    parsed: List[Dict[str, Any]] = []
    for index, line in enumerate(lines, start=1):
        try:
            obj = json.loads(line)
        except Exception as exc:
            return error_response(400, "INVALID_NDJSON", f"Event line {index} is malformed JSON: {exc}")
        errs = schema_errors(EVENT_VALIDATOR, obj)
        if errs:
            return error_response(422, "SCHEMA_VALIDATION_FAILED", f"Event line {index} failed Event v1.0 validation.", errs)
        parsed.append(obj)

    # Whole-batch behavior: inspect all conflicts before persisting any event.
    with _lock:
        for obj in parsed:
            event_id = obj["eventId"]
            payload_hash = canonical_hash(obj)
            previous = STATE["eventHashes"].get(event_id)
            if previous and previous != payload_hash:
                return error_response(409, "ID_PAYLOAD_CONFLICT", f"eventId {event_id} was reused with different content.")

        new_lines: List[str] = []
        for obj in parsed:
            event_id = obj["eventId"]
            payload_hash = canonical_hash(obj)
            if event_id not in STATE["eventHashes"]:
                STATE["eventHashes"][event_id] = payload_hash
                new_lines.append(json.dumps(obj, ensure_ascii=False, separators=(",", ":")))
            command_id = obj.get("commandId")
            if command_id:
                for record in STATE["commands"]:
                    if record["command"].get("commandId") != command_id:
                        continue
                    record["lastResultEventType"] = obj.get("eventType")
                    record["lastResultAtUtc"] = iso_z()
                    if obj.get("eventType") in TERMINAL_EVENT_TYPES:
                        record["terminalState"] = obj.get("eventType")
                        record["terminalAtUtc"] = iso_z()
                    break
        if new_lines:
            DATA_DIR.mkdir(parents=True, exist_ok=True)
            with EVENT_FILE.open("a", encoding="utf-8", newline="\n") as f:
                for line in new_lines:
                    f.write(line + "\n")
        STATE["lastEventAtUtc"] = iso_z()
        save_state()
    return Response(status_code=204)


@app.get("/mock/state")
def mock_state():
    with _lock:
        refresh_command_timeouts()
        commands = []
        for record in STATE["commands"][-30:]:
            commands.append({
                "command": record["command"],
                "deliveryCount": record.get("deliveryCount", 0),
                "lastDeliveredAtUtc": record.get("lastDeliveredAtUtc"),
                "lastResultEventType": record.get("lastResultEventType"),
                "terminalState": record.get("terminalState"),
                "terminalAtUtc": record.get("terminalAtUtc"),
            })
        return {
            "activeTarget": STATE.get("activeTarget"),
            "commands": commands,
            "receivedEventCount": len(STATE.get("eventHashes", {})),
            "receivedSnapshotCount": len(STATE.get("snapshotHashes", {})),
            "lastEventAtUtc": STATE.get("lastEventAtUtc"),
            "lastSnapshotAtUtc": STATE.get("lastSnapshotAtUtc"),
        }


@app.post("/mock/commands/set-area-policy")
async def mock_enqueue(request: Request):
    body = await request.json()
    area_id = str(body.get("areaId", "")).strip().upper()
    policy = str(body.get("policy", "")).strip().upper()
    if area_id not in {"A", "B", "C", "D"}:
        return error_response(422, "INVALID_AREA", "areaId must be A, B, C, or D.")
    if policy not in {"PRIORITY", "CLOSED", "RESTRICTED", "NORMAL"}:
        return error_response(422, "INVALID_POLICY", "policy must be PRIORITY, CLOSED, RESTRICTED, or NORMAL.")
    with _lock:
        target = STATE.get("activeTarget")
        if not target:
            return error_response(409, "NO_ACTIVE_UNITY_TARGET", "Start Unity Play and wait for at least one Command poll before creating a mock Command.")
        created = now_utc()
        expires_in = int(body.get("expiresInSeconds", 30))
        expires_in = max(1, min(expires_in, 600))
        command_id = "cmd_mock_" + created.strftime("%Y%m%dT%H%M%S%fZ") + "_" + uuid.uuid4().hex[:8]
        idempotency_key = str(uuid.uuid4())
        payload: Dict[str, Any] = {"areaId": area_id, "policy": policy}
        effective_seconds = body.get("effectiveForSeconds")
        if effective_seconds not in (None, "", 0, "0") and policy != "NORMAL":
            seconds = max(1, min(int(effective_seconds), 86400))
            payload["effectiveUntilUtc"] = iso_z(created + timedelta(seconds=seconds))
        reason = str(body.get("reason", "Mock Backend verification"))[:200]
        if reason:
            payload["reason"] = reason
        command = {
            "contractName": "smart-parking.command",
            "schemaVersion": "1.0",
            "commandId": command_id,
            "idempotencyKey": idempotency_key,
            "commandType": "SET_AREA_POLICY",
            "targetSourceId": target["sourceId"],
            "targetSessionId": target["sessionId"],
            "targetRunId": target["runId"],
            "createdAtUtc": iso_z(created),
            "expiresAtUtc": iso_z(created + timedelta(seconds=expires_in)),
            "priority": int(body.get("priority", 50)),
            "correlationId": "mock-admin-" + uuid.uuid4().hex[:12],
            "issuedBy": "mock-ui",
            "reason": reason,
            "payload": payload,
        }
        batch = {"contractName": "smart-parking.command-batch", "schemaVersion": "1.0", "serverTimeUtc": iso_z(), "leaseSeconds": 10, "commands": [command]}
        errs = schema_errors(COMMAND_BATCH_VALIDATOR, batch)
        if errs:
            return error_response(500, "MOCK_COMMAND_SCHEMA_ERROR", "Generated Command does not match the current Unity Command schema.", errs)
        STATE["commands"].append({
            "command": command,
            "deliveryCount": 0,
            "lastDeliveredAtUtc": None,
            "lastResultEventType": None,
            "lastResultAtUtc": None,
            "terminalState": None,
            "terminalAtUtc": None,
        })
        save_state()
        return {"created": True, "command": command}


@app.post("/mock/reset")
def mock_reset():
    with _lock:
        STATE.clear()
        STATE.update(default_state())
        save_state()
        if EVENT_FILE.exists():
            EVENT_FILE.unlink()
        if LATEST_SNAPSHOT_FILE.exists():
            LATEST_SNAPSHOT_FILE.unlink()
        for path in SNAPSHOT_DIR.glob("*.json"):
            path.unlink()
    return {"reset": True}


MOCK_HTML = r'''<!doctype html>
<html lang="ja"><head><meta charset="utf-8"><title>SmartParking P3 Mock Backend</title>
<style>body{font-family:system-ui,sans-serif;max-width:1100px;margin:24px auto;padding:0 16px}button{margin:4px;padding:8px 12px}select,input{padding:6px;margin:4px}pre{background:#111;color:#eee;padding:12px;overflow:auto;max-height:420px}.row{display:flex;gap:12px;flex-wrap:wrap}.card{border:1px solid #ccc;padding:14px;margin:12px 0;border-radius:8px}</style></head>
<body><h1>SmartParking P3 Mock Backend</h1>
<div class="card"><b>Unity target</b><div id="target">未検出。UnityをPlayしてPollingを開始してください。</div></div>
<div class="card"><h2>SET_AREA_POLICY</h2><div class="row">
<label>Area <select id="area"><option>A</option><option>B</option><option>C</option><option>D</option></select></label>
<label>Policy <select id="policy"><option>NORMAL</option><option>PRIORITY</option><option>CLOSED</option><option>RESTRICTED</option></select></label>
<label>Command期限(秒) <input id="expires" type="number" value="30" min="1" max="600"></label>
<label>Policy自動解除(秒・任意) <input id="effective" type="number" value="" min="1" max="86400"></label>
</div><button onclick="sendCommand()">Commandを登録</button> <span id="result"></span></div>
<div class="card"><button onclick="refresh()">状態更新</button> <button onclick="resetMock()">Mock状態を全消去</button><pre id="state"></pre></div>
<script>
async function refresh(){const r=await fetch('/mock/state');const s=await r.json();document.getElementById('target').textContent=s.activeTarget?JSON.stringify(s.activeTarget):'未検出。UnityをPlayしてPollingを開始してください。';document.getElementById('state').textContent=JSON.stringify(s,null,2)}
async function sendCommand(){const body={areaId:area.value,policy:policy.value,expiresInSeconds:Number(expires.value||30)};if(effective.value)body.effectiveForSeconds=Number(effective.value);const r=await fetch('/mock/commands/set-area-policy',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const j=await r.json();result.textContent=r.ok?'登録しました: '+j.command.commandId:'失敗: '+JSON.stringify(j);await refresh()}
async function resetMock(){if(!confirm('MockのCommand・受信履歴を消去しますか？'))return;await fetch('/mock/reset',{method:'POST'});await refresh()}
refresh();setInterval(refresh,2000);
</script></body></html>'''


@app.get("/mock", response_class=HTMLResponse)
def mock_page():
    return HTMLResponse(MOCK_HTML)
