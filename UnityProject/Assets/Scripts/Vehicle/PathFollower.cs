using System;
using System.Collections.Generic;
using UnityEngine;

public class PathFollower : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float turnSpeed = 8f;
    public float stoppingDistance = 0.25f;
    public bool rotateTowardsMovement = true;

    [Header("Debug")]
    public bool drawDebugPath = true;
    public int debugCurrentIndex;
    public float debugBlockedTimer;
    public float debugLastDeltaTime;
    public Vector3 debugCurrentTarget;
    public int debugUpdateCount;

    public event Action PathCompleted;
    public event Action<float> PathBlocked;

    private readonly List<Vector3> currentPath = new List<Vector3>();
    private int currentIndex;
    private bool isFollowing;
    private float blockedTimer;
    private VehicleCollisionShape collisionShape;

    public bool IsFollowing
    {
        get { return isFollowing; }
    }

    public void SetPath(IList<Vector3> path)
    {
        currentPath.Clear();
        currentIndex = 0;
        blockedTimer = 0f;
        UpdateDebugState(Vector3.zero);

        if (path == null || path.Count == 0)
        {
            isFollowing = false;
            return;
        }

        foreach (Vector3 point in path)
        {
            currentPath.Add(point);
        }

        isFollowing = true;
    }

    public void Stop()
    {
        isFollowing = false;
        currentPath.Clear();
        currentIndex = 0;
        blockedTimer = 0f;
        UpdateDebugState(Vector3.zero);
    }

    private void Update()
    {
        if (!isFollowing)
        {
            return;
        }

        debugUpdateCount++;

        if (currentIndex >= currentPath.Count)
        {
            CompletePath();
            return;
        }

        Vector3 targetPosition = currentPath[currentIndex];
        UpdateDebugState(targetPosition);
        Vector3 currentPosition = transform.position;
        Vector3 toTarget = targetPosition - currentPosition;
        toTarget.y = 0f;

        if (toTarget.magnitude <= stoppingDistance)
        {
            currentIndex++;
            UpdateDebugState(currentIndex < currentPath.Count ? currentPath[currentIndex] : Vector3.zero);
            if (currentIndex >= currentPath.Count)
            {
                CompletePath();
            }

            return;
        }

        if (IsForwardBlocked())
        {
            blockedTimer += Time.deltaTime;
            UpdateDebugState(targetPosition);
            PathBlocked?.Invoke(blockedTimer);
            return;
        }

        blockedTimer = 0f;
        UpdateDebugState(targetPosition);

        if (rotateTowardsMovement && toTarget.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        }

        Vector3 nextPosition = Vector3.MoveTowards(currentPosition, targetPosition, moveSpeed * Time.deltaTime);
        transform.position = nextPosition;
    }

    private void CompletePath()
    {
        isFollowing = false;
        currentPath.Clear();
        currentIndex = 0;
        UpdateDebugState(Vector3.zero);
        PathCompleted?.Invoke();
    }

    private void UpdateDebugState(Vector3 targetPosition)
    {
        debugCurrentIndex = currentIndex;
        debugBlockedTimer = blockedTimer;
        debugLastDeltaTime = Time.deltaTime;
        debugCurrentTarget = targetPosition;
    }

    private bool IsForwardBlocked()
    {
        if (collisionShape == null)
        {
            collisionShape = GetComponent<VehicleCollisionShape>();
        }

        if (collisionShape == null)
        {
            return false;
        }

        RaycastHit hit;
        return collisionShape.TryGetForwardObstacle(out hit);
    }

    private void OnDrawGizmos()
    {
        if (!drawDebugPath || currentPath.Count == 0)
        {
            return;
        }

        Gizmos.color = Color.green;
        Vector3 previousPoint = transform.position;

        for (int i = currentIndex; i < currentPath.Count; i++)
        {
            Gizmos.DrawSphere(currentPath[i], 0.4f);
            Gizmos.DrawLine(previousPoint, currentPath[i]);
            previousPoint = currentPath[i];
        }
    }
}
