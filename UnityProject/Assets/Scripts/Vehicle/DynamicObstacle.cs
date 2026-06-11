using System.Collections.Generic;
using UnityEngine;

public enum DynamicObstacleKind
{
    Vehicle,
    Pedestrian,
    StoppedVehicle,
    Other
}

public class DynamicObstacle : MonoBehaviour
{
    public string obstacleId;
    public DynamicObstacleKind kind = DynamicObstacleKind.Vehicle;
    public Vector3 velocity;
    public VehicleCollisionShape collisionShape;
    public List<string> plannedRoute = new List<string>();

    private Vector3 lastPosition;
    private bool hasLastPosition;

    public Vector3 Position
    {
        get { return transform.position; }
    }

    public Quaternion Rotation
    {
        get { return transform.rotation; }
    }

    private void Awake()
    {
        if (collisionShape == null)
        {
            collisionShape = GetComponent<VehicleCollisionShape>();
        }

        if (string.IsNullOrWhiteSpace(obstacleId))
        {
            Car car = GetComponent<Car>();
            obstacleId = car != null ? car.carId : gameObject.name;
        }

        lastPosition = transform.position;
        hasLastPosition = true;
    }

    private void Update()
    {
        if (!hasLastPosition)
        {
            lastPosition = transform.position;
            hasLastPosition = true;
            return;
        }

        if (Time.deltaTime > 0f)
        {
            velocity = (transform.position - lastPosition) / Time.deltaTime;
        }

        lastPosition = transform.position;
    }
}
