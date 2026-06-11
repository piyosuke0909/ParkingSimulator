using UnityEngine;

public class Car : MonoBehaviour
{
    public string carId = "Car_001";
    public float length = 4.5f;
    public float width = 1.8f;
    public NPCDrivingState currentState = NPCDrivingState.Idle;
}
