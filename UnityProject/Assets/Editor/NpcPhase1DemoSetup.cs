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
    private const int VerificationMaxFrames = 2400;
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
        string followerState = pathFollower != null ? $"{pathFollower.IsFollowing}, updates={pathFollower.debugUpdateCount}, target={pathFollower.debugCurrentTarget:F2}" : "missing";
        string collisionState = collisionShape != null ? $"{collisionShape.bodyLength:F2}x{collisionShape.bodyWidth:F2}x{collisionShape.bodyHeight:F2}" : "missing";

        Debug.Log($"NPC Phase 1 play state: isPlaying={EditorApplication.isPlaying}, isPaused={EditorApplication.isPaused}, timeScale={Time.timeScale:F2}, frame={Time.frameCount}, carPosition={position}, npcState={npcState}, pathFollowing={followerState}, collision={collisionState}");
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
        Transform reverseEntryPoint = EnsurePoint(targetSlot, "NPC_Demo_ReverseEntryPoint", -targetSlot.transform.forward * 6f + targetSlot.transform.right * 2.5f, false, carObject.transform.position.y);

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
        carObject.transform.position = entrancePosition;

        RoadNode entranceNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_Entrance", entrancePosition);
        RoadNode middleNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_Mid", Vector3.Lerp(entrancePosition, approachPoint.position, 0.5f));
        RoadNode slotNode = EnsureRoadNode(waypointsRoot, "NPC_Demo_SlotApproach", approachPoint.position);
        EnsureDemoDrivableArea(entrancePosition, middleNode.transform.position, approachPoint.position, frontEntryPoint.position, reverseEntryPoint.position, parkingPoint.position);

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

        ConfigureCarNpcCollision(carObject);

        EnsureComponent<DynamicObstacle>(carObject);

        ParkingAction parkingAction = EnsureComponent<ParkingAction>(carObject);
        parkingAction.parkingSpeed = 2.5f;

        NPCDriver npcDriver = EnsureComponent<NPCDriver>(carObject);
        npcDriver.routePlanner = routePlanner;
        npcDriver.assignedSlot = targetSlot;
        npcDriver.useAssignedSlotOnly = true;
        npcDriver.startOnPlay = true;
        npcDriver.usePhase2SafetyChecks = true;
        npcDriver.requireDrivableAreaForManeuver = true;
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
                Debug.Log($"NPC Phase 1 verification passed in {verificationFrameCount} frames. Car_NPC reached Parked.");
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
