using UnityEngine;

[ExecuteAlways]
public class TrafficGizmoSettings : MonoBehaviour
{
    public static TrafficGizmoSettings Instance
    {
        get;
        private set;
    }

    [Header("Master")]
    public bool drawTrafficGizmos = true;

    [Header("Waypoint")]
    public bool drawWaypointMarkers = true;
    public bool drawWaypointConnections = false;

    [Header("Merge Point")]
    public bool drawMergePoints = true;
    public bool drawMergePointsOnlyWhenSelected = true;

    [Header("Play Mode")]
    public bool hideWaypointConnectionsInPlayMode = true;
    public bool forceSelectedMergeOnlyInPlayMode = true;

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnValidate()
    {
        Instance = this;
    }

    public bool ShouldDrawWaypointMarker()
    {
        return drawTrafficGizmos &&
               drawWaypointMarkers;
    }

    public bool ShouldDrawWaypointConnections()
    {
        if (!drawTrafficGizmos ||
            !drawWaypointConnections)
        {
            return false;
        }

        if (Application.isPlaying &&
            hideWaypointConnectionsInPlayMode)
        {
            return false;
        }

        return true;
    }

    public bool ShouldDrawMergePoint()
    {
        return drawTrafficGizmos &&
               drawMergePoints;
    }

    public bool ShouldDrawMergeOnlyWhenSelected()
    {
        if (Application.isPlaying &&
            forceSelectedMergeOnlyInPlayMode)
        {
            return true;
        }

        return drawMergePointsOnlyWhenSelected;
    }

    [ContextMenu("Performance Preset")]
    public void ApplyPerformancePreset()
    {
        drawTrafficGizmos = true;
        drawWaypointMarkers = true;
        drawWaypointConnections = false;
        drawMergePoints = true;
        drawMergePointsOnlyWhenSelected = true;
        hideWaypointConnectionsInPlayMode = true;
        forceSelectedMergeOnlyInPlayMode = true;
    }

    [ContextMenu("Full Debug Preset")]
    public void ApplyFullDebugPreset()
    {
        drawTrafficGizmos = true;
        drawWaypointMarkers = true;
        drawWaypointConnections = true;
        drawMergePoints = true;
        drawMergePointsOnlyWhenSelected = false;
        hideWaypointConnectionsInPlayMode = false;
        forceSelectedMergeOnlyInPlayMode = false;
    }

    [ContextMenu("Hide All Traffic Gizmos")]
    public void HideAllTrafficGizmos()
    {
        drawTrafficGizmos = false;
    }
}
