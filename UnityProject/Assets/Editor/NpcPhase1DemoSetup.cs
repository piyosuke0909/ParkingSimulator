using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class NpcPhase1DemoSetup
{
    private const string ScenePath = "Assets/Scenes/Parking/SampleScene.unity";
    private const string MarkerAssetPath = "Assets/Editor/NPCPhase1DemoSetup.run";
    private const string MarkerRelativePath = "Editor/NPCPhase1DemoSetup.run";

    static NpcPhase1DemoSetup()
    {
        EditorApplication.delayCall += RunIfRequested;
        EditorApplication.update += RunIfRequested;
    }

    [MenuItem("Parking Simulator/NPC Phase 1/Setup Demo Scene")]
    public static void SetupDemoSceneMenu()
    {
        SetupDemoScene(false);
    }

    [MenuItem("Parking Simulator/NPC Phase 1/Setup Demo Scene And Play")]
    public static void SetupDemoSceneAndPlayMenu()
    {
        SetupDemoScene(true);
    }

    private static void RunIfRequested()
    {
        string markerPath = Path.Combine(Application.dataPath, MarkerRelativePath);
        if (!File.Exists(markerPath))
        {
            return;
        }

        EditorApplication.update -= RunIfRequested;

        File.Delete(markerPath);

        string markerMetaPath = markerPath + ".meta";
        if (File.Exists(markerMetaPath))
        {
            File.Delete(markerMetaPath);
        }

        AssetDatabase.Refresh();
        SetupDemoScene(true);
    }

    private static void SetupDemoScene(bool enterPlayMode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        OpenSampleSceneIfNeeded();

        GameObject carObject = GameObject.Find("DummyCar_001");
        if (carObject == null)
        {
            Debug.LogWarning("NPC Phase 1 demo setup failed: DummyCar_001 was not found.");
            return;
        }

        ParkingSlot targetSlot = FindNearestAvailableSlot(carObject.transform.position);
        if (targetSlot == null)
        {
            Debug.LogWarning("NPC Phase 1 demo setup failed: no available ParkingSlot was found.");
            return;
        }

        Transform parkingPoint = EnsurePoint(targetSlot, "NPC_Demo_ParkingPoint", Vector3.zero, true, carObject.transform.position.y);
        Transform approachPoint = EnsurePoint(targetSlot, "NPC_Demo_ApproachPoint", -targetSlot.transform.forward * 8f, false, carObject.transform.position.y);
        Transform frontEntryPoint = EnsurePoint(targetSlot, "NPC_Demo_FrontEntryPoint", -targetSlot.transform.forward * 3f, false, carObject.transform.position.y);
        Transform reverseEntryPoint = EnsurePoint(targetSlot, "NPC_Demo_ReverseEntryPoint", -targetSlot.transform.forward * 3f + targetSlot.transform.right * 1.5f, false, carObject.transform.position.y);

        targetSlot.parkingPoint = parkingPoint;
        targetSlot.approachPoint = approachPoint;
        targetSlot.frontEntryPoint = frontEntryPoint;
        targetSlot.reverseEntryPoint = reverseEntryPoint;
        targetSlot.slotWidth = 2.5f;
        targetSlot.slotDepth = 5.0f;
        targetSlot.aisleWidth = 6.0f;
        targetSlot.allowFrontIn = true;
        targetSlot.allowReverseIn = true;
        targetSlot.preferredManeuver = ParkingManeuverPreference.Auto;
        ParkingSlotGeometry slotGeometry = EnsureComponent<ParkingSlotGeometry>(targetSlot.gameObject);
        slotGeometry.allowedVehicleMargin = 0.2f;
        slotGeometry.BuildRectangleFromSlot(targetSlot);
        targetSlot.geometry = slotGeometry;

        Transform waypointsRoot = EnsureRoot("Waypoints");
        GameObject graphObject = EnsureGameObject("NPC_Phase1_RouteSystem");
        RoadGraph roadGraph = EnsureComponent<RoadGraph>(graphObject);
        RoutePlanner routePlanner = EnsureComponent<RoutePlanner>(graphObject);

        Vector3 entrancePosition = ResolveEntrancePosition(carObject.transform.position);
        carObject.transform.position = entrancePosition;

        RoadNode entranceNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_Entrance", entrancePosition);
        RoadNode middleNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_Mid", Vector3.Lerp(entrancePosition, approachPoint.position, 0.5f));
        RoadNode slotNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_SlotApproach", approachPoint.position);

        ConnectOneWay(entranceNode, middleNode);
        ConnectOneWay(middleNode, slotNode);

        roadGraph.nodesRoot = waypointsRoot;
        roadGraph.treatConnectionsAsBidirectional = true;
        roadGraph.AutoCollectNodes();

        routePlanner.roadGraph = roadGraph;
        routePlanner.entranceNode = entranceNode;
        routePlanner.useEntranceNodeAsDefaultStart = true;

        targetSlot.roadNodeId = slotNode.nodeId;

        PathFollower pathFollower = EnsureComponent<PathFollower>(carObject);
        pathFollower.moveSpeed = 8f;
        pathFollower.turnSpeed = 8f;
        pathFollower.drawDebugPath = true;

        VehicleCollisionShape collisionShape = EnsureComponent<VehicleCollisionShape>(carObject);
        collisionShape.bodyLength = 4.5f;
        collisionShape.bodyWidth = 1.8f;
        collisionShape.bodyHeight = 1.6f;
        collisionShape.sideSafetyMargin = 0.2f;
        collisionShape.frontSafetyMargin = 0.6f;
        collisionShape.rearSafetyMargin = 0.2f;

        EnsureComponent<DynamicObstacle>(carObject);

        ParkingAction parkingAction = EnsureComponent<ParkingAction>(carObject);
        parkingAction.parkingSpeed = 2.5f;

        NPCDriver npcDriver = EnsureComponent<NPCDriver>(carObject);
        npcDriver.routePlanner = routePlanner;
        npcDriver.assignedSlot = targetSlot;
        npcDriver.useAssignedSlotOnly = true;
        npcDriver.startOnPlay = true;
        npcDriver.usePhase2SafetyChecks = true;
        npcDriver.requireDrivableAreaForManeuver = false;
        npcDriver.logStateChanges = true;

        GameObject parkingAreas = GameObject.Find("ParkingAreas");
        if (parkingAreas != null)
        {
            npcDriver.parkingSlotsRoot = parkingAreas.transform;
        }

        Selection.activeGameObject = carObject;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"NPC Phase 1 demo setup complete. Car={carObject.name}, Slot={targetSlot.slotId}");

        if (enterPlayMode)
        {
            EditorApplication.isPlaying = true;
        }
    }

    private static void OpenSampleSceneIfNeeded()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path == ScenePath)
        {
            return;
        }

        if (File.Exists(Path.Combine(Application.dataPath, "../", ScenePath)))
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
    }

    private static ParkingSlot FindNearestAvailableSlot(Vector3 position)
    {
        ParkingSlot[] slots = UnityEngine.Object.FindObjectsOfType<ParkingSlot>();
        ParkingSlot nearestSlot = null;
        float nearestDistance = float.MaxValue;

        foreach (ParkingSlot slot in slots)
        {
            if (slot == null || !slot.IsAvailable())
            {
                continue;
            }

            float distance = Vector3.SqrMagnitude(slot.transform.position - position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSlot = slot;
            }
        }

        return nearestSlot;
    }

    private static Transform EnsureRoot(string rootName)
    {
        GameObject rootObject = GameObject.Find(rootName);
        if (rootObject == null)
        {
            rootObject = new GameObject(rootName);
        }

        return rootObject.transform;
    }

    private static GameObject EnsureGameObject(string objectName)
    {
        GameObject gameObject = GameObject.Find(objectName);
        if (gameObject == null)
        {
            gameObject = new GameObject(objectName);
        }

        return gameObject;
    }

    private static Transform EnsurePoint(ParkingSlot slot, string pointName, Vector3 localOffset, bool alignToSlot, float worldY)
    {
        Transform point = slot.transform.Find(pointName);
        if (point == null)
        {
            point = new GameObject(pointName).transform;
            point.SetParent(slot.transform);
        }

        Vector3 worldPosition = slot.transform.position + localOffset;
        worldPosition.y = worldY;
        point.position = worldPosition;

        if (alignToSlot)
        {
            point.rotation = slot.transform.rotation;
        }
        else
        {
            point.rotation = Quaternion.LookRotation((slot.transform.position - point.position).normalized, Vector3.up);
        }

        return point;
    }

    private static Vector3 ResolveEntrancePosition(Vector3 fallbackPosition)
    {
        GameObject entranceObject = GameObject.Find("Entrance_Road_01");
        if (entranceObject == null)
        {
            return fallbackPosition;
        }

        Vector3 entrancePosition = entranceObject.transform.position;
        entrancePosition.y = fallbackPosition.y;
        return entrancePosition;
    }

    private static RoadNode EnsureRoadNode(Transform root, string nodeId, Vector3 position)
    {
        Transform nodeTransform = root.Find(nodeId);
        if (nodeTransform == null)
        {
            nodeTransform = new GameObject(nodeId).transform;
            nodeTransform.SetParent(root);
        }

        nodeTransform.position = position;
        RoadNode roadNode = EnsureComponent<RoadNode>(nodeTransform.gameObject);
        roadNode.nodeId = nodeId;
        return roadNode;
    }

    private static void ConnectOneWay(RoadNode fromNode, RoadNode toNode)
    {
        if (fromNode == null || toNode == null)
        {
            return;
        }

        fromNode.connectedNodes.Clear();
        fromNode.connectedNodes.Add(toNode);
    }

    private static T EnsureComponent<T>(GameObject gameObject) where T : Component
    {
        T component = gameObject.GetComponent<T>();
        if (component == null)
        {
            component = gameObject.AddComponent<T>();
        }

        return component;
    }
}
