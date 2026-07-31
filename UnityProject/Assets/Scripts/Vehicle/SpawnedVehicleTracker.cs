using UnityEngine;

public class SpawnedVehicleTracker : MonoBehaviour
{
    private VehicleSpawnManager owner;
    private bool notified;

    public bool HasNotifiedRemoval => notified;

    public void Initialize(VehicleSpawnManager spawnManager)
    {
        owner = spawnManager;
        notified = false;
    }

    public void NotifyExited()
    {
        NotifyOwner("exit_route_completed");
    }

    private void OnDestroy()
    {
        NotifyOwner("vehicle_destroyed");
    }

    private void NotifyOwner(string reason)
    {
        if (notified)
        {
            return;
        }

        notified = true;

        if (owner != null)
        {
            owner.NotifyVehicleRemoved(gameObject, reason);
        }
    }
}
