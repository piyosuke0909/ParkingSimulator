using System.Collections.Generic;
using UnityEngine;


public enum WaypointTrafficRole
{
    [Tooltip("自動合流検出の対象外です。")]
    Unspecified,

    [Tooltip("横方向の一方通行本線です。")]
    Mainline,

    [Tooltip("上下の本線を結ぶ縦方向の連絡レーンです。")]
    ConnectorLane,

    [Tooltip("白丸の駐車位置です。通常の合流検出対象外です。")]
    ParkingPoint
}

/// <summary>
/// 本線・駐車レーン共通の1台分交通セルとして使用するWaypointです。
///
/// Reserved By:
/// 次に到着する車
///
/// Occupied By:
/// 現在そのセルにいる車
///
/// 同じWaypointへ複数経路が合流する場合も、
/// Target Waypointの予約によって同時進入を防ぎます。
/// </summary>
public class Waypoint : MonoBehaviour
{
    [Header("Waypoint Info")]
    public string waypointId;

    [Header("Connections")]
    public List<Waypoint> nextWaypoints =
        new List<Waypoint>();

    [Header("Settings")]
    public bool isStopPoint;
    public bool isIntersection;
    public bool isEntrance;
    public bool isExit;

    [Header("Traffic Auto Setup")]
    [Tooltip("横方向の黒丸はMainline、縦方向の黒丸はConnectorLane、白丸はParkingPointを設定します。")]
    public WaypointTrafficRole trafficRole =
        WaypointTrafficRole.Unspecified;

    [Tooltip("横方向6レーンと各縦連絡レーンを区別するIDです。同じ一方向レーン内で統一してください。")]
    public int laneGroupId;

    [Header("Runtime State")]
    public NPC_CarController reservedBy;
    public NPC_CarController occupiedBy;

    [Header("Debug")]
    public bool logReservationDebug;

    [Header("Gizmo - Color Only")]
    public bool drawWaypointMarker = true;
    public bool drawConnections = true;

    [Min(0.05f)]
    public float markerRadius = 0.5f;

    [Min(0f)]
    public float connectionHeight = 0.2f;

    [Range(1f, 8f)]
    public float lineWidth = 2.5f;

    [Min(0.1f)]
    public float arrowLength = 1.1f;

    [Range(5f, 80f)]
    public float arrowAngle = 25f;

    public Color freeConnectionColor =
        new Color(
            0.2f,
            0.85f,
            1f,
            1f
        );

    public Color reservedConnectionColor =
        new Color(
            0.2f,
            0.45f,
            1f,
            1f
        );

    public Color occupiedConnectionColor =
        new Color(
            1f,
            0.2f,
            0.1f,
            1f
        );

    public NPC_CarController BlockingCar
    {
        get
        {
            return
                occupiedBy != null
                    ? occupiedBy
                    : reservedBy;
        }
    }

    private void OnValidate()
    {
        if (nextWaypoints == null)
        {
            nextWaypoints =
                new List<Waypoint>();
        }

        markerRadius =
            Mathf.Max(
                0.05f,
                markerRadius
            );

        lineWidth =
            Mathf.Clamp(
                lineWidth,
                1f,
                8f
            );

        arrowLength =
            Mathf.Max(
                0.1f,
                arrowLength
            );
    }

    public bool TryReserve(
        NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (reservedBy != null &&
            reservedBy != car)
        {
            return false;
        }

        if (occupiedBy != null &&
            occupiedBy != car)
        {
            return false;
        }

        reservedBy = car;

        if (logReservationDebug)
        {
            Debug.Log(
                $"{name}: Reserved by {car.name}",
                this
            );
        }

        return true;
    }

    public bool TryEnter(
        NPC_CarController car)
    {
        if (car == null)
        {
            return false;
        }

        if (reservedBy != null &&
            reservedBy != car)
        {
            return false;
        }

        if (occupiedBy != null &&
            occupiedBy != car)
        {
            return false;
        }

        if (reservedBy == car)
        {
            reservedBy = null;
        }

        occupiedBy = car;

        if (logReservationDebug)
        {
            Debug.Log(
                $"{name}: Occupied by {car.name}",
                this
            );
        }

        return true;
    }

    public void Enter(
        NPC_CarController car)
    {
        TryEnter(car);
    }

    public void ReleaseReservation(
        NPC_CarController car)
    {
        if (car != null &&
            reservedBy == car)
        {
            reservedBy = null;
        }
    }

    public void ReleaseOccupancy(
        NPC_CarController car)
    {
        if (car != null &&
            occupiedBy == car)
        {
            occupiedBy = null;
        }
    }

    public void Release(
        NPC_CarController car)
    {
        ReleaseReservation(car);
        ReleaseOccupancy(car);
    }

    public bool IsAvailableFor(
        NPC_CarController car)
    {
        return
            car != null &&
            (reservedBy == null ||
             reservedBy == car) &&
            (occupiedBy == null ||
             occupiedBy == car);
    }

    private Color GetMarkerColor()
    {
        if (occupiedBy != null)
        {
            return occupiedConnectionColor;
        }

        if (reservedBy != null)
        {
            return reservedConnectionColor;
        }

        if (isEntrance)
        {
            return Color.green;
        }

        if (isExit)
        {
            return
                new Color(
                    1f,
                    0.45f,
                    0f,
                    1f
                );
        }

        if (isIntersection)
        {
            return Color.yellow;
        }

        return freeConnectionColor;
    }

    private void OnDrawGizmos()
    {
        TrafficGizmoSettings global =
            TrafficGizmoSettings.Instance;

        bool shouldDrawMarker =
            global != null
                ? global.ShouldDrawWaypointMarker()
                : drawWaypointMarker;

        bool shouldDrawConnections =
            global != null
                ? global.ShouldDrawWaypointConnections()
                : drawConnections;

        if (shouldDrawMarker)
        {
            Gizmos.color =
                GetMarkerColor();

            Gizmos.DrawSphere(
                transform.position,
                markerRadius
            );
        }

        if (!shouldDrawConnections ||
            nextWaypoints == null)
        {
            return;
        }

        foreach (Waypoint next
                 in nextWaypoints)
        {
            if (next == null)
            {
                continue;
            }

            DrawConnection(next);
        }
    }

    private void DrawConnection(
        Waypoint next)
    {
        Color color =
            next.occupiedBy != null
                ? occupiedConnectionColor
                : next.reservedBy != null
                    ? reservedConnectionColor
                    : freeConnectionColor;

        Vector3 from =
            transform.position +
            Vector3.up *
            connectionHeight;

        Vector3 to =
            next.transform.position +
            Vector3.up *
            connectionHeight;

#if UNITY_EDITOR
        UnityEditor.Handles.color = color;

        UnityEditor.Handles.DrawAAPolyLine(
            lineWidth,
            from,
            to
        );

        DrawArrow(
            from,
            to,
            color
        );
#else
        Gizmos.color = color;
        Gizmos.DrawLine(from, to);
#endif
    }

#if UNITY_EDITOR
    private void DrawArrow(
        Vector3 from,
        Vector3 to,
        Color color)
    {
        Vector3 direction =
            to - from;

        direction.y = 0f;

        float length =
            direction.magnitude;

        if (length <= 0.01f)
        {
            return;
        }

        direction /= length;

        Vector3 tip =
            Vector3.Lerp(
                from,
                to,
                0.72f
            );

        Quaternion rightRotation =
            Quaternion.LookRotation(
                direction,
                Vector3.up
            ) *
            Quaternion.Euler(
                0f,
                180f +
                arrowAngle,
                0f
            );

        Quaternion leftRotation =
            Quaternion.LookRotation(
                direction,
                Vector3.up
            ) *
            Quaternion.Euler(
                0f,
                180f -
                arrowAngle,
                0f
            );

        UnityEditor.Handles.color = color;

        UnityEditor.Handles.DrawAAPolyLine(
            lineWidth,
            tip,
            tip +
            rightRotation *
            Vector3.forward *
            arrowLength
        );

        UnityEditor.Handles.DrawAAPolyLine(
            lineWidth,
            tip,
            tip +
            leftRotation *
            Vector3.forward *
            arrowLength
        );
    }
#endif
}
