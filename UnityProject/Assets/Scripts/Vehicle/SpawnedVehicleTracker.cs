using UnityEngine;

public class SpawnedVehicleTracker : MonoBehaviour
{
    private VehicleSpawnManager owner;
    private bool notified;

    public void Initialize(VehicleSpawnManager spawnManager)
    {
        owner = spawnManager;
        notified = false;
    }

    private void OnDestroy()
    {
        if (notified)
        {
            return;
        }

        notified = true;

        if (owner != null)
        {
            owner.NotifyVehicleDestroyed(gameObject);
        }
    }
}
