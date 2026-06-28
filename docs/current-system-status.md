# Current System Status

Last checked: 2026-06-28

## Premise

Treat the Unity side as the completed simulation source of truth.

Unity is responsible for:

- running the parking lot demo
- spawning NPC cars
- choosing and reserving parking slots
- moving cars through waypoints
- detecting occupied / empty slots
- holding Unity-side runtime state such as slot state, car state, reservations, routes, and heatmap data

Backend and frontend are not yet implemented as working services. They should be built around the Unity data that already exists.

## Unity Project

Project path:

```text
UnityProject/
```

Unity version:

```text
2022.3.62f2
```

Active scene confirmed through Unity MCP:

```text
Assets/Scenes/Parking/SampleScene.unity
```

Scene status:

- active scene: `SampleScene`
- loaded: yes
- dirty: no
- root count: 1
- `ParkingLotManager` exists in the scene
- `VehicleSpawnManager` exists in the scene

Important Unity scripts:

| File | Role |
| --- | --- |
| `UnityProject/Assets/Scripts/Parking/ParkingSlot.cs` | Single parking slot state, owner, sensor flag, visual color |
| `UnityProject/Assets/Scripts/Parking/ParkingLotManager.cs` | Collects all slots, queries by ID / area / state, reserves and releases slots |
| `UnityProject/Assets/Scripts/Parking/CameraParkingSensor.cs` | Detects cars at slots using raycast and overlap box, then updates `sensorOccupied` |
| `UnityProject/Assets/Scripts/Vehicle/CarNPC/NPC_CarController.cs` | NPC car lifecycle: route driving, parking, parked wait, backing out, leaving |
| `UnityProject/Assets/Scripts/Vehicle/CarNPC/VehicleSpawnManager.cs` | Spawns NPC cars, picks available slots, assigns routes |
| `UnityProject/Assets/Scripts/Vehicle/WaypointRouteManager.cs` | Finds routes through waypoints by BFS |
| `UnityProject/Assets/Scripts/Vehicle/Waypoint.cs` | Waypoint graph and reservation state |
| `UnityProject/Assets/Scripts/Vehicle/RoadSection.cs` | Road section reservation |
| `UnityProject/Assets/Scripts/Vehicle/TrafficBlock.cs` | Conflict block reservation / occupancy |
| `UnityProject/Assets/Scripts/Heatmap/ImageBasedHeatmapManager.cs` | Heatmap based on car transforms or image processing |

## Unity Data Available Now

Unity already has enough runtime state to power a web dashboard.

Parking slot data:

- `slotId`
- `areaId`
- `state`: `Empty`, `Occupied`, `Reserved`, `Leaving`, `Disabled`
- `sensorOccupied`
- `isLeaving`
- `reservedBy`
- `occupiedBy`
- `parkingPoint` world position
- `detectionPoint` world position
- `accessWaypoint`

Parking lot aggregate data:

- all slots
- slot lookup by ID
- slots by area
- slots by state
- counts by state
- first / random available slot
- available slots with valid access waypoint

Vehicle data:

- `carId`
- current NPC state: `Idle`, `DrivingRoute`, `Parking`, `Parked`, `WaitingToBackOut`, `BackingOut`, `Leaving`, `Finished`
- current position / rotation
- target parking slot
- route to parking
- route to exit
- front-car stop state
- waypoint / road / traffic-block reservation state

Traffic and route data:

- waypoint graph
- route result as waypoint list
- road section reservation
- traffic block reservation and occupancy

Heatmap data:

- heat values are held inside `ImageBasedHeatmapManager`
- current implementation renders a Unity texture
- it does not yet export heatmap values or images to backend

## Current Backend / Frontend Status

Current `WebApp/backend` contents are documentation and placeholder folders only:

- `models/.gitkeep`
- `routers/.gitkeep`
- `services/.gitkeep`
- README files

There is no working FastAPI app yet:

- no `main.py`
- no route implementation
- no database model
- no WebSocket or streaming endpoint
- no Unity ingest endpoint

Current `WebApp/frontend` contents are also placeholders:

- `src/components/.gitkeep`
- `src/pages/.gitkeep`
- `src/hooks/.gitkeep`
- `src/types/.gitkeep`
- README files

There is no working React app yet:

- no `package.json`
- no `App.tsx`
- no user screen
- no admin screen
- no API client
- no realtime state subscription

## Main Integration Gap

Unity has runtime state, but there is currently no bridge from Unity to backend.

Missing Unity-side bridge:

- no network script in `UnityProject/Assets/Scripts/Network`
- no `UnityWebRequest` sender
- no WebSocket client
- no data snapshot DTOs
- no camera/video publishing pipeline

Missing backend bridge:

- no endpoint to receive Unity snapshots
- no endpoint to receive slot events
- no endpoint to receive car events
- no process manager to launch the Unity demo
- no API for frontend user/admin screens

Missing frontend bridge:

- no polling or WebSocket client
- no parking map display
- no user-facing availability response
- no admin monitoring view

## Recommended Architecture

Use Unity as producer, backend as state server, frontend as consumer.

```text
Unity demo / Unity build
        |
        | POST snapshots / events
        v
Backend API
        |
        | REST for initial state
        | WebSocket or SSE for realtime updates
        v
Frontend
        |
        | User screen
        | Admin screen
```

For the MVP, do not make the backend scrape Unity internals directly. Add a Unity exporter that sends a compact snapshot every fixed interval and sends events when important state changes.

## Suggested MVP Data Contract

Unity to backend snapshot:

```json
{
  "timestamp": "2026-06-28T00:00:00Z",
  "scene": "SampleScene",
  "slots": [
    {
      "slotId": "A-01",
      "areaId": "A",
      "state": "Empty",
      "sensorOccupied": false,
      "isLeaving": false,
      "position": { "x": 0, "y": 0, "z": 0 },
      "accessWaypointId": "WP-001",
      "reservedByCarId": null,
      "occupiedByCarId": null
    }
  ],
  "cars": [
    {
      "carId": "NPC_Car_001",
      "state": "DrivingRoute",
      "position": { "x": 0, "y": 0, "z": 0 },
      "rotationY": 90,
      "targetSlotId": "A-01"
    }
  ],
  "summary": {
    "empty": 10,
    "reserved": 2,
    "occupied": 4,
    "leaving": 1,
    "disabled": 0
  }
}
```

Backend to frontend user response:

```json
{
  "recommendedSlot": {
    "slotId": "A-01",
    "areaId": "A",
    "state": "Empty"
  },
  "summary": {
    "empty": 10,
    "reserved": 2,
    "occupied": 4
  }
}
```

Backend to frontend admin response:

```json
{
  "scene": "SampleScene",
  "slots": [],
  "cars": [],
  "summary": {},
  "updatedAt": "2026-06-28T00:00:00Z"
}
```

## Video / Demo Playback

There are two separate needs:

1. Start the Unity demo.
2. Show video or visual status on the frontend.

Recommended MVP:

- build the Unity project as a Windows standalone app
- backend starts/stops the Unity build as a child process
- Unity sends state snapshots to backend over HTTP
- frontend shows a 2D map and state table first

Recommended video step after MVP:

- add a Unity camera output path
- choose one transport:
  - periodic screenshot upload for simple preview
  - MJPEG endpoint for simple browser video
  - WebRTC for lower-latency live video

Do not block the state API on video. Parking availability should work even if video streaming is disabled.

## Frontend Screens To Build

User screen:

- current availability summary
- recommended parking slot
- area filter
- simple parking map
- route / guidance placeholder from entrance to slot

Admin screen:

- full slot list and state
- car list and current movement state
- reservations and occupied owners
- Unity demo process status
- last Unity snapshot timestamp
- video preview when available
- alerts for stale Unity data

## Backend Endpoints To Build First

Unity ingest:

```text
POST /api/unity/snapshot
POST /api/unity/events
```

Frontend read APIs:

```text
GET /api/parking/status
GET /api/parking/slots
GET /api/parking/recommendation
GET /api/admin/state
```

Realtime:

```text
GET /api/ws/state
```

Unity process control:

```text
POST /api/admin/unity/start
POST /api/admin/unity/stop
GET  /api/admin/unity/status
```

Video later:

```text
GET /api/admin/unity/video
```

## Implementation Order

1. Add backend skeleton with health check and in-memory state store.
2. Add `POST /api/unity/snapshot`.
3. Add Unity snapshot exporter under `UnityProject/Assets/Scripts/Network`.
4. Add frontend admin state view.
5. Add frontend user recommendation view.
6. Add WebSocket or SSE updates.
7. Add backend Unity process start/stop using a built Unity executable.
8. Add video preview after state integration is stable.

## Current Conclusion

Unity can be treated as complete for the parking simulation side.

The next actual product work is not more Unity scene work. It is the integration layer:

- Unity state exporter
- backend ingest and state API
- frontend user/admin screens
- optional video transport after state data is reliable

