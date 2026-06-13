using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class NpcPhase1DemoSetup
{
    private const string ScenePath = "Assets/Scenes/Parking/SampleScene.unity";
    private const string CarNpcPrefabPath = "Assets/Prefabs/Cars/Car_NPC/Car_NPC.prefab";
    private const string CarNpcVisualPrefabPath = "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Prefabs/Mesh Only/Sports Car.prefab";
    private const string MarkerAssetPath = "Assets/Editor/NPCPhase1DemoSetup.run";
    private const string MarkerRelativePath = "Editor/NPCPhase1DemoSetup.run";
    private const int VerificationMaxFrames = 6000;
    private const string VerificationRequestedKey = "NpcPhase1DemoSetup.VerificationRequested";
    private const string VerificationFrameCountKey = "NpcPhase1DemoSetup.VerificationFrameCount";
    private const string CarLayerName = "Car";
    private const float CarNpcBodyLength = 10.0f;
    private const float CarNpcBodyWidth = 4.5f;
    private const float CarNpcBodyHeight = 3.0f;
    private const float CarNpcVisualScale = 3.2f;
    private const float CarNpcVisualCenterOffsetZ = -0.93f;
    private const float DemoSlotWidth = 6.0f;
    private const float DemoSlotDepth = 11.5f;
    private const float DemoSlotAisleWidth = 12.0f;
    private const float DemoRoadWidth = 12.0f;
    private const float DemoLeftLaneOffset = DemoRoadWidth * 0.25f;
    private const float DemoCruiseSpeed = 18.0f;
    private const float DemoExitWaitSeconds = 5.0f;
    private static readonly string[] CarNpcPaintMaterialPaths =
    {
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Black Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Blue Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Gold Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Green Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Light Blue Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Purple Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Red Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Silver Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/White Paint.mat",
        "Assets/Models/Cars/NPCCarComplete/Free Sports Car/Materials/Paint/Yellow Paint.mat",
    };

    private static bool verificationRunning;
    private static int verificationFrameCount;

    static NpcPhase1DemoSetup()
    {
        EditorApplication.delayCall += RunIfRequested;
        EditorApplication.update += RunIfRequested;

        if (SessionState.GetBool(VerificationRequestedKey, false))
        {
            EditorApplication.update += RunParkingVerification;
        }
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

    [MenuItem("Parking Simulator/NPC Phase 1/Resume Play")]
    public static void ResumePlayMenu()
    {
        Time.timeScale = 1f;
        EditorApplication.isPaused = false;
    }

    [MenuItem("Parking Simulator/NPC Phase 1/Stop Play")]
    public static void StopPlayMenu()
    {
        Time.timeScale = 1f;
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = false;
    }

    [MenuItem("Parking Simulator/NPC Phase 1/Log Play State")]
    public static void LogPlayStateMenu()
    {
        GameObject carObject = GameObject.Find("Car_NPC");
        NPCDriver npcDriver = carObject != null ? carObject.GetComponent<NPCDriver>() : null;
        PathFollower pathFollower = carObject != null ? carObject.GetComponent<PathFollower>() : null;
        VehicleCollisionShape collisionShape = carObject != null ? carObject.GetComponent<VehicleCollisionShape>() : null;

        string position = carObject != null ? carObject.transform.position.ToString("F2") : "missing";
        string npcState = npcDriver != null ? npcDriver.state.ToString() : "missing";
        string exitWait = npcDriver != null ? npcDriver.debugExitWaitTimer.ToString("F1") : "missing";
        string routeCount = npcDriver != null ? npcDriver.assignedRoute.Count.ToString() : "missing";
        string followerState = pathFollower != null ? $"{pathFollower.IsFollowing}, updates={pathFollower.debugUpdateCount}, target={pathFollower.debugCurrentTarget:F2}" : "missing";
        string collisionState = collisionShape != null ? $"{collisionShape.bodyLength:F2}x{collisionShape.bodyWidth:F2}x{collisionShape.bodyHeight:F2}" : "missing";

        Debug.Log($"NPC Phase 1 play state: isPlaying={EditorApplication.isPlaying}, isPaused={EditorApplication.isPaused}, timeScale={Time.timeScale:F2}, frame={Time.frameCount}, carPosition={position}, npcState={npcState}, exitWait={exitWait}, routeCount={routeCount}, pathFollowing={followerState}, collision={collisionState}");
    }

    [MenuItem("Parking Simulator/NPC Phase 1/Verify Demo Parking")]
    public static void VerifyDemoParkingMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("NPC Phase 1 verification can be started from Edit Mode only.");
            return;
        }

        SetupDemoScene(false);
        verificationRunning = false;
        verificationFrameCount = 0;
        SessionState.SetBool(VerificationRequestedKey, true);
        SessionState.SetInt(VerificationFrameCountKey, 0);
        EditorApplication.update -= RunParkingVerification;
        EditorApplication.update += RunParkingVerification;
        Time.timeScale = 1f;
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = true;
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
        EnsureCarNpcPrefabVisual();

        GameObject carObject = ResolveDemoCarObject();
        if (carObject == null)
        {
            Debug.LogWarning("NPC Phase 1 demo setup failed: Car_NPC prefab or DummyCar_001 was not found.");
            return;
        }

        ParkingSlot targetSlot = FindNearestAvailableSlot(carObject.transform.position);
        if (targetSlot == null)
        {
            Debug.LogWarning("NPC Phase 1 demo setup failed: no available ParkingSlot was found.");
            return;
        }

        Transform parkingPoint = EnsurePoint(targetSlot, "NPC_Demo_ParkingPoint", Vector3.zero, true, carObject.transform.position.y);
        Transform approachPoint = EnsurePoint(targetSlot, "NPC_Demo_ApproachPoint", -targetSlot.transform.forward * 14f, false, carObject.transform.position.y);
        Transform frontEntryPoint = EnsurePoint(targetSlot, "NPC_Demo_FrontEntryPoint", -targetSlot.transform.forward * 6f, false, carObject.transform.position.y);
        Transform reverseEntryPoint = EnsurePoint(targetSlot, "NPC_Demo_ReverseEntryPoint", targetSlot.transform.forward * 7f, true, carObject.transform.position.y);

        targetSlot.parkingPoint = parkingPoint;
        targetSlot.approachPoint = approachPoint;
        targetSlot.frontEntryPoint = frontEntryPoint;
        targetSlot.reverseEntryPoint = reverseEntryPoint;
        targetSlot.slotWidth = DemoSlotWidth;
        targetSlot.slotDepth = DemoSlotDepth;
        targetSlot.aisleWidth = DemoSlotAisleWidth;
        targetSlot.allowFrontIn = true;
        targetSlot.allowReverseIn = true;
        targetSlot.preferredManeuver = ParkingManeuverPreference.Auto;
        ParkingSlotGeometry slotGeometry = EnsureComponent<ParkingSlotGeometry>(targetSlot.gameObject);
        slotGeometry.allowedVehicleMargin = 0.25f;
        slotGeometry.BuildRectangleFromSlot(targetSlot);
        targetSlot.geometry = slotGeometry;

        EnsureManeuverPath(targetSlot, "NPC_Demo_FrontInPath", ParkingManeuverType.FrontIn, false, frontEntryPoint, parkingPoint);
        EnsureManeuverPath(targetSlot, "NPC_Demo_ReverseInPath", ParkingManeuverType.ReverseIn, true, reverseEntryPoint, parkingPoint);

        Transform waypointsRoot = EnsureRoot("Waypoints");
        GameObject graphObject = EnsureGameObject("NPC_Phase1_RouteSystem");
        RoadGraph roadGraph = EnsureComponent<RoadGraph>(graphObject);
        RoutePlanner routePlanner = EnsureComponent<RoutePlanner>(graphObject);

        Vector3 entrancePosition = ResolveEntrancePosition(carObject.transform.position);
        Vector3 exitPosition = ResolveExitPosition(carObject.transform.position);
        carObject.transform.position = entrancePosition;

        RoadNode entranceNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_Entrance", entrancePosition);
        RoadNode middleNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_Mid", Vector3.Lerp(entrancePosition, approachPoint.position, 0.5f));
        RoadNode slotNode = EnsureRoadNode(waypointsRoot, "Lane_C_01_Aisle", BuildLeftLaneAislePoint(approachPoint.position));
        RoadNode legacySlotNode = FindRoadNode(waypointsRoot, "NPC_Demo_SlotApproach");
        RoadNode laneExitNode01 = EnsureRoadNode(waypointsRoot, "Lane_C_Exit_01", BuildLeftLanePoint(approachPoint.position, exitPosition, 0.35f));
        RoadNode laneExitNode02 = EnsureRoadNode(waypointsRoot, "Lane_C_Exit_02", BuildLeftLanePoint(approachPoint.position, exitPosition, 1f));
        RoadNode mainExitNode01 = EnsureRoadNode(waypointsRoot, "Lane_Main_Exit_01", BuildMainLeftLanePoint(approachPoint.position, exitPosition, 0.45f));
        RoadNode mainExitNode02 = EnsureRoadNode(waypointsRoot, "Lane_Main_Exit_02", BuildExitApproachPoint(exitPosition, approachPoint.position.y));
        RoadNode exitNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_ExitGate", BuildExitGatePoint(exitPosition, approachPoint.position.y));
        RoadNode publicRoadMergeNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_PublicRoadMerge", BuildPublicRoadMergePoint(exitPosition, approachPoint.position.y));
        RoadNode legacyExitNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_Exit", exitNode.Position);
        EnsureDemoDrivableArea(
            entrancePosition,
            middleNode.transform.position,
            slotNode.transform.position,
            laneExitNode01.transform.position,
            laneExitNode02.transform.position,
            mainExitNode01.transform.position,
            mainExitNode02.transform.position,
            frontEntryPoint.position,
            reverseEntryPoint.position,
            parkingPoint.position,
            exitPosition);

        ConnectOneWay(entranceNode, middleNode);
        ConnectOneWay(middleNode, slotNode);
        DisconnectOneWay(middleNode, legacySlotNode);
        DisconnectOneWay(middleNode, legacyExitNode);
        ConnectOneWay(slotNode, laneExitNode01);
        ConnectOneWay(laneExitNode01, laneExitNode02);
        ConnectOneWay(laneExitNode02, mainExitNode01);
        ConnectOneWay(mainExitNode01, mainExitNode02);
        DisconnectOneWay(mainExitNode02, legacyExitNode);
        ConnectOneWay(mainExitNode02, exitNode);
        ConnectOneWay(exitNode, publicRoadMergeNode);

        Transform roadEdgesRoot = EnsureChildRoot(graphObject.transform, "RoadEdges");
        EnsureRoadEdge(roadEdgesRoot, "Edge_Entrance_To_Mid", entranceNode, middleNode);
        EnsureRoadEdge(roadEdgesRoot, "Edge_Mid_To_Lane_C_01_Aisle", middleNode, slotNode);
        EnsureRoadEdge(roadEdgesRoot, "Edge_Lane_C_01_Aisle_To_Lane_C_Exit_01", slotNode, laneExitNode01);
        EnsureRoadEdge(roadEdgesRoot, "Edge_Lane_C_Exit_01_To_Lane_C_Exit_02", laneExitNode01, laneExitNode02);
        EnsureRoadEdge(
            roadEdgesRoot,
            "Edge_Lane_C_Exit_02_To_Lane_Main_Exit_01",
            laneExitNode02,
            mainExitNode01,
            BuildLeftTurnPathPoints(laneExitNode02.Position, mainExitNode01.Position));
        EnsureRoadEdge(roadEdgesRoot, "Edge_Lane_Main_Exit_01_To_Lane_Main_Exit_02", mainExitNode01, mainExitNode02);
        EnsureRoadEdge(roadEdgesRoot, "Edge_Lane_Main_Exit_02_To_Exit", mainExitNode02, exitNode);
        EnsureRoadEdge(roadEdgesRoot, "Edge_ExitGate_To_PublicRoadMerge", exitNode, publicRoadMergeNode);
        EnsureDemoRouteValidationAreas(slotNode, laneExitNode02, mainExitNode01, mainExitNode02, exitNode, slotGeometry);

        roadGraph.nodesRoot = waypointsRoot;
        roadGraph.edgesRoot = roadEdgesRoot;
        roadGraph.treatConnectionsAsBidirectional = true;
        roadGraph.useRoadEdgesWhenAvailable = true;
        roadGraph.AutoCollect();

        routePlanner.roadGraph = roadGraph;
        routePlanner.entranceNode = entranceNode;
        routePlanner.useEntranceNodeAsDefaultStart = true;

        targetSlot.roadNodeId = slotNode.nodeId;
        EditorUtility.SetDirty(targetSlot);
        EditorUtility.SetDirty(slotGeometry);
        EditorUtility.SetDirty(roadGraph);
        EditorUtility.SetDirty(routePlanner);

        PathFollower pathFollower = EnsureComponent<PathFollower>(carObject);
        pathFollower.moveSpeed = DemoCruiseSpeed;
        pathFollower.turnSpeed = 8f;
        pathFollower.drawDebugPath = true;

        ConfigureCarNpcCollision(carObject);

        EnsureComponent<DynamicObstacle>(carObject);

        ParkingAction parkingAction = EnsureComponent<ParkingAction>(carObject);
        parkingAction.parkingSpeed = 2.5f;

        NPCDriver npcDriver = EnsureComponent<NPCDriver>(carObject);
        npcDriver.routePlanner = routePlanner;
        npcDriver.assignedSlot = targetSlot;
        npcDriver.exitNode = exitNode;
        npcDriver.useAssignedSlotOnly = true;
        npcDriver.startOnPlay = true;
        npcDriver.usePhase2SafetyChecks = true;
        npcDriver.requireDrivableAreaForManeuver = true;
        npcDriver.validateExitRouteAreas = true;
        npcDriver.requireLaneDrivableAreaForExit = true;
        npcDriver.autoExitAfterParking = true;
        npcDriver.exitWaitSeconds = DemoExitWaitSeconds;
        npcDriver.deactivateOnExit = false;
        npcDriver.logStateChanges = true;

        GameObject parkingAreas = GameObject.Find("ParkingAreas");
        if (parkingAreas != null)
        {
            npcDriver.parkingSlotsRoot = parkingAreas.transform;
        }

        EnsureCarNpcVisual(carObject);
        ApplyRandomPaint(carObject);
        ConvertDemoCarsToNpcVisuals();

        Selection.activeGameObject = carObject;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"NPC Phase 1 demo setup complete. Car={carObject.name}, Slot={targetSlot.slotId}");

        if (enterPlayMode)
        {
            Time.timeScale = 1f;
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }
    }

    private static void RunParkingVerification()
    {
        if (!SessionState.GetBool(VerificationRequestedKey, false))
        {
            EditorApplication.update -= RunParkingVerification;
            return;
        }

        if (!EditorApplication.isPlaying)
        {
            return;
        }

        verificationRunning = true;
        verificationFrameCount = SessionState.GetInt(VerificationFrameCountKey, 0);
        EditorApplication.isPaused = false;
        Time.timeScale = 1f;

        GameObject carObject = GameObject.Find("Car_NPC");
        NPCDriver npcDriver = carObject != null ? carObject.GetComponent<NPCDriver>() : null;

        if (npcDriver != null)
        {
            if (npcDriver.state == NPCDrivingState.Parked)
            {
                if (!npcDriver.autoExitAfterParking)
                {
                    Debug.Log($"NPC Phase 1 verification passed in {verificationFrameCount} frames. Car_NPC reached Parked.");
                    StopParkingVerification();
                    return;
                }
            }

            if (npcDriver.state == NPCDrivingState.Exited)
            {
                Debug.Log($"NPC Phase 3 verification passed in {verificationFrameCount} frames. Car_NPC reached Exited.");
                StopParkingVerification();
                return;
            }

            if (npcDriver.state == NPCDrivingState.Blocked)
            {
                Debug.LogWarning($"NPC Phase 1 verification failed after {verificationFrameCount} frames. Car_NPC is Blocked.");
                StopParkingVerification();
                return;
            }
        }

        if (verificationRunning && verificationFrameCount >= VerificationMaxFrames)
        {
            string stateName = npcDriver != null ? npcDriver.state.ToString() : "missing NPCDriver";
            Debug.LogWarning($"NPC Phase 1 verification timed out after {verificationFrameCount} frames. State={stateName}.");
            StopParkingVerification();
            return;
        }

        verificationFrameCount++;
        SessionState.SetInt(VerificationFrameCountKey, verificationFrameCount);
    }

    private static void StopParkingVerification()
    {
        verificationRunning = false;
        SessionState.SetBool(VerificationRequestedKey, false);
        SessionState.SetInt(VerificationFrameCountKey, 0);
        EditorApplication.update -= RunParkingVerification;
        EditorApplication.isPaused = false;
    }

    private static GameObject ResolveDemoCarObject()
    {
        GameObject existingCarNpc = GameObject.Find("Car_NPC");
        if (existingCarNpc != null)
        {
            return existingCarNpc;
        }

        GameObject dummyCar = GameObject.Find("DummyCar_001");
        GameObject carNpcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarNpcPrefabPath);
        if (carNpcPrefab == null)
        {
            return dummyCar;
        }

        GameObject carNpc = PrefabUtility.InstantiatePrefab(carNpcPrefab) as GameObject;
        if (carNpc == null)
        {
            return dummyCar;
        }

        carNpc.name = "Car_NPC";

        if (dummyCar != null)
        {
            carNpc.transform.position = dummyCar.transform.position;
            carNpc.transform.rotation = dummyCar.transform.rotation;
            dummyCar.SetActive(false);
        }

        return carNpc;
    }

    private static void EnsureCarNpcVisual(GameObject carObject)
    {
        if (carObject == null)
        {
            return;
        }

        GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarNpcVisualPrefabPath);
        if (visualPrefab == null)
        {
            Debug.LogWarning($"Car_NPC visual prefab was not found: {CarNpcVisualPrefabPath}");
            return;
        }

        for (int i = carObject.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = carObject.transform.GetChild(i);
            if (child.name == "Visual")
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        GameObject visualObject = PrefabUtility.InstantiatePrefab(visualPrefab, carObject.transform) as GameObject;
        if (visualObject == null)
        {
            Debug.LogWarning($"Car_NPC visual prefab could not be instantiated: {CarNpcVisualPrefabPath}");
            return;
        }

        visualObject.name = "Visual";
        visualObject.layer = carObject.layer;
        SetLayerRecursively(visualObject.transform, carObject.layer);

        visualObject.transform.localPosition = new Vector3(0f, 0f, CarNpcVisualCenterOffsetZ);
        visualObject.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        visualObject.transform.localScale = new Vector3(CarNpcVisualScale, CarNpcVisualScale, CarNpcVisualScale);
    }

    private static void ConvertDemoCarsToNpcVisuals()
    {
        foreach (GameObject demoCar in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (demoCar == null || !demoCar.scene.IsValid() || !demoCar.name.StartsWith("DummyCar_"))
            {
                continue;
            }

            demoCar.transform.localScale = Vector3.one;
            ConfigureCarNpcCollision(demoCar);

            DynamicObstacle obstacle = EnsureComponent<DynamicObstacle>(demoCar);
            obstacle.obstacleId = demoCar.name;

            EnsureCarNpcVisual(demoCar);
            ApplyRandomPaint(demoCar);
            SetLegacyDemoRenderersVisible(demoCar, false);
        }
    }

    private static void ApplyRandomPaint(GameObject carObject)
    {
        Material paintMaterial = LoadRandomPaintMaterial();
        if (carObject == null || paintMaterial == null)
        {
            return;
        }

        Transform visual = carObject.transform.Find("Visual");
        if (visual == null)
        {
            return;
        }

        MeshRenderer bodyRenderer = null;
        foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.gameObject.name == "Body")
            {
                bodyRenderer = renderer;
                break;
            }
        }

        if (bodyRenderer == null)
        {
            return;
        }

        Material[] materials = bodyRenderer.sharedMaterials;
        if (materials.Length == 0)
        {
            return;
        }

        materials[0] = paintMaterial;
        bodyRenderer.sharedMaterials = materials;
    }

    private static Material LoadRandomPaintMaterial()
    {
        if (CarNpcPaintMaterialPaths.Length == 0)
        {
            return null;
        }

        int startIndex = UnityEngine.Random.Range(0, CarNpcPaintMaterialPaths.Length);
        for (int i = 0; i < CarNpcPaintMaterialPaths.Length; i++)
        {
            int index = (startIndex + i) % CarNpcPaintMaterialPaths.Length;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(CarNpcPaintMaterialPaths[index]);
            if (material != null)
            {
                return material;
            }
        }

        return null;
    }

    private static void SetLegacyDemoRenderersVisible(GameObject demoCar, bool visible)
    {
        foreach (Renderer renderer in demoCar.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsUnderVisual(renderer.transform))
            {
                renderer.enabled = visible;
            }
        }
    }

    private static bool IsUnderVisual(Transform transform)
    {
        while (transform != null)
        {
            if (transform.name == "Visual")
            {
                return true;
            }

            transform = transform.parent;
        }

        return false;
    }

    private static void EnsureCarNpcPrefabVisual()
    {
        GameObject carNpcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarNpcPrefabPath);
        if (carNpcPrefab == null)
        {
            return;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(CarNpcPrefabPath);
        try
        {
            EnsureCarNpcVisual(prefabRoot);
            ConfigureCarNpcCollision(prefabRoot);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, CarNpcPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void ConfigureCarNpcCollision(GameObject carObject)
    {
        if (carObject == null)
        {
            return;
        }

        int carLayer = ResolveCarLayer();
        if (carLayer >= 0)
        {
            SetLayerRecursively(carObject.transform, carLayer);
        }

        Car car = EnsureComponent<Car>(carObject);
        car.length = CarNpcBodyLength;
        car.width = CarNpcBodyWidth;

        BoxCollider boxCollider = EnsureComponent<BoxCollider>(carObject);
        boxCollider.size = new Vector3(CarNpcBodyWidth, CarNpcBodyHeight, CarNpcBodyLength);
        boxCollider.center = new Vector3(0f, CarNpcBodyHeight * 0.5f, 0f);

        VehicleCollisionShape collisionShape = EnsureComponent<VehicleCollisionShape>(carObject);
        collisionShape.bodyLength = CarNpcBodyLength;
        collisionShape.bodyWidth = CarNpcBodyWidth;
        collisionShape.bodyHeight = CarNpcBodyHeight;
        collisionShape.sideSafetyMargin = 0.2f;
        collisionShape.frontSafetyMargin = 0.6f;
        collisionShape.rearSafetyMargin = 0.2f;
        collisionShape.obstacleLayerMask = ResolveCarObstacleMask();

        CarObstacleDetection obstacleDetection = EnsureComponent<CarObstacleDetection>(carObject);
        obstacleDetection.sensorHeight = CarNpcBodyHeight * 0.5f;
        obstacleDetection.sensorWidth = CarNpcBodyWidth * 0.4f;
        obstacleDetection.frontSensorZ = CarNpcBodyLength * 0.48f;
        obstacleDetection.rearSensorZ = -CarNpcBodyLength * 0.48f;
        obstacleDetection.obstacleMask = ResolveCarObstacleMask();

        DynamicObstacle obstacle = EnsureComponent<DynamicObstacle>(carObject);
        obstacle.kind = DynamicObstacleKind.Vehicle;
        obstacle.collisionShape = collisionShape;

        NPCDriver npcDriver = carObject.GetComponent<NPCDriver>();
        if (npcDriver != null)
        {
            npcDriver.usePhase2SafetyChecks = true;
            npcDriver.requireDrivableAreaForManeuver = true;
        }
    }

    private static int ResolveCarLayer()
    {
        return LayerMask.NameToLayer(CarLayerName);
    }

    private static LayerMask ResolveCarObstacleMask()
    {
        int carLayer = ResolveCarLayer();
        if (carLayer < 0)
        {
            return 0;
        }

        return 1 << carLayer;
    }

    private static void EnsureManeuverPath(ParkingSlot slot, string pathName, ParkingManeuverType maneuverType, bool requiresReverse, params Transform[] controlPoints)
    {
        if (slot == null)
        {
            return;
        }

        Transform pathTransform = slot.transform.Find(pathName);
        if (pathTransform == null)
        {
            pathTransform = new GameObject(pathName).transform;
            pathTransform.SetParent(slot.transform);
        }

        pathTransform.localPosition = Vector3.zero;
        pathTransform.localRotation = Quaternion.identity;

        ManeuverPath path = EnsureComponent<ManeuverPath>(pathTransform.gameObject);
        path.pathId = pathName;
        path.slotId = slot.slotId;
        path.maneuverType = maneuverType;
        path.requiresReverse = requiresReverse;
        path.estimatedDuration = requiresReverse ? 5f : 4f;
        path.requiredClearance = 0.2f;
        path.controlPoints = controlPoints;
    }

    private static void EnsureDemoDrivableArea(params Vector3[] points)
    {
        if (points == null || points.Length == 0)
        {
            return;
        }

        GameObject areaObject = EnsureGameObject("NPC_Demo_DrivableArea");
        DrivableArea area = EnsureComponent<DrivableArea>(areaObject);
        area.areaId = "NPC_Demo_DrivableArea";
        area.areaType = DrivableAreaType.ParkingApproach;
        area.defaultSpeedLimit = 8f;
        area.allowStop = true;
        area.priority = 100;

        float minX = points[0].x;
        float maxX = points[0].x;
        float minZ = points[0].z;
        float maxZ = points[0].z;

        foreach (Vector3 point in points)
        {
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minZ = Mathf.Min(minZ, point.z);
            maxZ = Mathf.Max(maxZ, point.z);
        }

        const float margin = 12f;
        minX -= margin;
        maxX += margin;
        minZ -= margin;
        maxZ += margin;

        area.polygon = new[]
        {
            new Vector3(minX, 0f, minZ),
            new Vector3(maxX, 0f, minZ),
            new Vector3(maxX, 0f, maxZ),
            new Vector3(minX, 0f, maxZ)
        };
    }

    private static void EnsureDemoRouteValidationAreas(
        RoadNode slotNode,
        RoadNode laneExitNode,
        RoadNode mainExitNode,
        RoadNode mainExitEndNode,
        RoadNode exitNode,
        ParkingSlotGeometry slotGeometry)
    {
        Transform root = EnsureRoot("NPC_Demo_RouteValidationAreas");

        float laneHalfWidth = Mathf.Max(CarNpcBodyLength * 0.5f + 3.0f, CarNpcBodyWidth * 0.5f + 2.5f);
        float y = 0f;

        if (slotNode != null && laneExitNode != null)
        {
            float minZ = Mathf.Min(slotNode.Position.z, laneExitNode.Position.z) - laneHalfWidth;
            float maxZ = Mathf.Max(slotNode.Position.z, laneExitNode.Position.z) + laneHalfWidth;
            EnsureDrivableArea(
                root,
                "DrivableArea_C_Aisle",
                "DrivableArea_C_Aisle",
                DrivableAreaType.Lane,
                BuildAxisAlignedPolygon(
                    slotNode.Position.x - laneHalfWidth,
                    slotNode.Position.x + laneHalfWidth,
                    minZ,
                    maxZ,
                    y));
        }

        if (laneExitNode != null && mainExitNode != null && mainExitEndNode != null)
        {
            float minX = Mathf.Min(laneExitNode.Position.x, Mathf.Min(mainExitNode.Position.x, mainExitEndNode.Position.x)) - laneHalfWidth;
            float maxX = Mathf.Max(exitNode != null ? exitNode.Position.x : mainExitEndNode.Position.x, Mathf.Max(mainExitNode.Position.x, mainExitEndNode.Position.x)) + laneHalfWidth;
            EnsureDrivableArea(
                root,
                "DrivableArea_Main_Exit",
                "DrivableArea_Main_Exit",
                DrivableAreaType.Lane,
                BuildAxisAlignedPolygon(
                    minX,
                    maxX,
                    mainExitNode.Position.z - laneHalfWidth,
                    mainExitNode.Position.z + laneHalfWidth,
                    y));
        }

        if (exitNode != null)
        {
            EnsureDrivableArea(
                root,
                "DrivableArea_Exit",
                "DrivableArea_Exit",
                DrivableAreaType.Exit,
                BuildAxisAlignedPolygon(
                    exitNode.Position.x - laneHalfWidth,
                    exitNode.Position.x + laneHalfWidth,
                    exitNode.Position.z - laneHalfWidth,
                    exitNode.Position.z + laneHalfWidth,
                    y));
        }

        if (slotGeometry != null && slotGeometry.corners != null && slotGeometry.corners.Length >= 3)
        {
            EnsureNoDriveArea(root, "NoDriveArea_Slot_C_01", "NoDriveArea_Slot_C_01", slotGeometry.corners);
        }
    }

    private static void EnsureDrivableArea(Transform root, string objectName, string areaId, DrivableAreaType areaType, Vector3[] polygon)
    {
        Transform areaTransform = root.Find(objectName);
        if (areaTransform == null)
        {
            areaTransform = new GameObject(objectName).transform;
            areaTransform.SetParent(root);
        }

        areaTransform.localPosition = Vector3.zero;
        areaTransform.localRotation = Quaternion.identity;
        areaTransform.localScale = Vector3.one;

        DrivableArea area = EnsureComponent<DrivableArea>(areaTransform.gameObject);
        area.areaId = areaId;
        area.areaType = areaType;
        area.defaultSpeedLimit = DemoCruiseSpeed;
        area.allowStop = true;
        area.priority = 200;
        area.drawDebug = true;
        area.debugColor = areaType == DrivableAreaType.Exit ? new Color(0.1f, 0.9f, 0.3f, 1f) : new Color(0f, 0.7f, 1f, 1f);
        area.polygon = polygon;
    }

    private static void EnsureNoDriveArea(Transform root, string objectName, string areaId, Vector3[] polygon)
    {
        Transform areaTransform = root.Find(objectName);
        if (areaTransform == null)
        {
            areaTransform = new GameObject(objectName).transform;
            areaTransform.SetParent(root);
        }

        areaTransform.localPosition = Vector3.zero;
        areaTransform.localRotation = Quaternion.identity;
        areaTransform.localScale = Vector3.one;

        NoDriveArea area = EnsureComponent<NoDriveArea>(areaTransform.gameObject);
        area.areaId = areaId;
        area.active = true;
        area.drawDebug = true;
        area.debugColor = new Color(1f, 0.15f, 0.05f, 1f);
        area.polygon = polygon;
    }

    private static Vector3[] BuildAxisAlignedPolygon(float minX, float maxX, float minZ, float maxZ, float y)
    {
        return new[]
        {
            new Vector3(minX, y, minZ),
            new Vector3(maxX, y, minZ),
            new Vector3(maxX, y, maxZ),
            new Vector3(minX, y, maxZ)
        };
    }

    private static void SetLayerRecursively(Transform target, int layer)
    {
        target.gameObject.layer = layer;

        for (int i = 0; i < target.childCount; i++)
        {
            SetLayerRecursively(target.GetChild(i), layer);
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

    private static Transform EnsureChildRoot(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child == null)
        {
            child = new GameObject(childName).transform;
            child.SetParent(parent);
        }

        child.localPosition = Vector3.zero;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        return child;
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

    private static Vector3 ResolveExitPosition(Vector3 fallbackPosition)
    {
        GameObject exitObject = GameObject.Find("Exit_Road_01");
        if (exitObject == null)
        {
            return fallbackPosition;
        }

        Vector3 exitPosition = exitObject.transform.position;
        exitPosition.y = fallbackPosition.y;
        return exitPosition;
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

    private static RoadNode FindRoadNode(Transform root, string nodeId)
    {
        if (root == null)
        {
            return null;
        }

        Transform nodeTransform = root.Find(nodeId);
        return nodeTransform != null ? nodeTransform.GetComponent<RoadNode>() : null;
    }

    private static RoadEdge EnsureRoadEdge(Transform root, string edgeId, RoadNode fromNode, RoadNode toNode)
    {
        return EnsureRoadEdge(root, edgeId, fromNode, toNode, new Vector3[0]);
    }

    private static RoadEdge EnsureRoadEdge(Transform root, string edgeId, RoadNode fromNode, RoadNode toNode, Vector3[] pathPoints)
    {
        Transform edgeTransform = root.Find(edgeId);
        if (edgeTransform == null)
        {
            edgeTransform = new GameObject(edgeId).transform;
            edgeTransform.SetParent(root);
        }

        edgeTransform.localPosition = Vector3.zero;
        edgeTransform.localRotation = Quaternion.identity;
        edgeTransform.localScale = Vector3.one;

        RoadEdge edge = EnsureComponent<RoadEdge>(edgeTransform.gameObject);
        edge.edgeId = edgeId;
        edge.fromNodeId = fromNode != null ? fromNode.nodeId : string.Empty;
        edge.toNodeId = toNode != null ? toNode.nodeId : string.Empty;
        edge.oneWay = true;
        edge.blocked = false;
        edge.speedLimit = DemoCruiseSpeed;
        edge.costMultiplier = 1f;
        edge.laneSide = RoadLaneSide.LeftHandTraffic;
        edge.laneWidth = DemoRoadWidth * 0.5f;
        edge.roadWidth = DemoRoadWidth;
        edge.turnRadius = Mathf.Max(3f, DemoLeftLaneOffset);
        edge.pathPoints = pathPoints ?? new Vector3[0];
        return edge;
    }

    private static Vector3 BuildLeftLaneAislePoint(Vector3 aislePosition)
    {
        return new Vector3(aislePosition.x - DemoLeftLaneOffset, aislePosition.y, aislePosition.z);
    }

    private static Vector3 BuildLanePoint(Vector3 aislePosition, Vector3 exitPosition, float t)
    {
        float exitLaneZ = exitPosition.z - 4f;
        return new Vector3(aislePosition.x, aislePosition.y, Mathf.Lerp(aislePosition.z, exitLaneZ, t));
    }

    private static Vector3 BuildLeftLanePoint(Vector3 aislePosition, Vector3 exitPosition, float t)
    {
        Vector3 centerPoint = BuildLanePoint(aislePosition, exitPosition, t);
        centerPoint.x -= DemoLeftLaneOffset;
        return centerPoint;
    }

    private static Vector3 BuildMainExitPoint(Vector3 aislePosition, Vector3 exitPosition, float t)
    {
        float exitLaneZ = exitPosition.z - 4f;
        return new Vector3(Mathf.Lerp(aislePosition.x, exitPosition.x, t), aislePosition.y, exitLaneZ);
    }

    private static Vector3 BuildMainLeftLanePoint(Vector3 aislePosition, Vector3 exitPosition, float t)
    {
        Vector3 centerPoint = BuildMainExitPoint(aislePosition, exitPosition, t);
        centerPoint.x -= DemoLeftLaneOffset;
        centerPoint.z += DemoLeftLaneOffset;
        return centerPoint;
    }

    private static Vector3 BuildExitApproachPoint(Vector3 exitPosition, float y)
    {
        return new Vector3(exitPosition.x - 8f, y, BuildExitLaneZ(exitPosition));
    }

    private static Vector3 BuildExitGatePoint(Vector3 exitPosition, float y)
    {
        return new Vector3(exitPosition.x, y, BuildExitLaneZ(exitPosition));
    }

    private static Vector3 BuildPublicRoadMergePoint(Vector3 exitPosition, float y)
    {
        return new Vector3(exitPosition.x, y, exitPosition.z);
    }

    private static Vector3[] BuildLeftTurnPathPoints(Vector3 fromPosition, Vector3 toPosition)
    {
        return new[]
        {
            new Vector3(fromPosition.x, fromPosition.y, toPosition.z)
        };
    }

    private static float BuildExitLaneZ(Vector3 exitPosition)
    {
        return exitPosition.z - 4f + DemoLeftLaneOffset;
    }

    private static void ConnectOneWay(RoadNode fromNode, RoadNode toNode)
    {
        if (fromNode == null || toNode == null)
        {
            return;
        }

        if (!fromNode.connectedNodes.Contains(toNode))
        {
            fromNode.connectedNodes.Add(toNode);
        }
    }

    private static void DisconnectOneWay(RoadNode fromNode, RoadNode toNode)
    {
        if (fromNode == null || toNode == null)
        {
            return;
        }

        fromNode.connectedNodes.Remove(toNode);
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
