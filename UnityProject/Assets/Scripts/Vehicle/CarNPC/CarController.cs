using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Movement Limits")]
    public float maxForwardSpeed = 18f;
    public float maxReverseSpeed = 8f;
    public float acceleration = 10f;
    public float braking = 16f;
    public float coastDrag = 4f;

    [Header("Steering")]
    public float turnSpeed = 75f;
    public float maxSteerAngle = 30f;
    public float wheelBase = 2.5f;
    public float steeringResponse = 4.5f;
    public float minimumSteerSpeed = 0.35f;
    public float highSpeedSteerFactor = 0.35f;
    public float lateralGrip = 7f;

    [Header("Input")]
    public KeyCode forwardKey = KeyCode.W;
    public KeyCode backwardKey = KeyCode.S;
    public KeyCode leftKey = KeyCode.A;
    public KeyCode rightKey = KeyCode.D;
    public bool enableManualInput;
    public bool enablePhysicsDrive;

    public float Throttle { get; private set; }
    public float Steering { get; private set; }
    public float Brake { get; private set; }
    public float CurrentSpeed { get; private set; }

    private Rigidbody carRigidbody;

    private void Awake()
    {
        carRigidbody = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        if (!enableManualInput)
        {
            return;
        }

        float throttle = 0f;
        if (Input.GetKey(forwardKey))
        {
            throttle += 1f;
        }

        if (Input.GetKey(backwardKey))
        {
            throttle -= 1f;
        }

        float steering = 0f;
        if (Input.GetKey(leftKey))
        {
            steering -= 1f;
        }

        if (Input.GetKey(rightKey))
        {
            steering += 1f;
        }

        SetInput(throttle, steering, throttle == 0f ? 0f : Brake);
    }

    private void FixedUpdate()
    {
        if (carRigidbody == null)
        {
            return;
        }

        CurrentSpeed = Vector3.Dot(carRigidbody.velocity, transform.forward);

        if (!enablePhysicsDrive)
        {
            return;
        }

        ApplyPhysicsDrive();
    }

    public void SetInput(float throttle, float steering, float brake = 0f)
    {
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steering = Mathf.Clamp(steering, -1f, 1f);
        Brake = Mathf.Clamp01(brake);
    }

    public void ClearInput()
    {
        SetInput(0f, 0f, 0f);
    }

    private void ApplyPhysicsDrive()
    {
        float targetSpeed = Throttle >= 0f ? maxForwardSpeed * Throttle : maxReverseSpeed * Throttle;
        float speedDelta = targetSpeed - CurrentSpeed;
        float accelRate = Mathf.Abs(Throttle) > 0.01f ? acceleration : coastDrag;
        Vector3 force = transform.forward * Mathf.Clamp(speedDelta, -accelRate, accelRate);
        carRigidbody.AddForce(force, ForceMode.Acceleration);

        if (Brake > 0f)
        {
            carRigidbody.AddForce(-carRigidbody.velocity.normalized * braking * Brake, ForceMode.Acceleration);
        }

        float speedFactor = Mathf.InverseLerp(0f, maxForwardSpeed, Mathf.Abs(CurrentSpeed));
        float steeringFactor = Mathf.Lerp(1f, highSpeedSteerFactor, speedFactor);
        float yaw = Steering * turnSpeed * steeringFactor * Time.fixedDeltaTime;
        if (Mathf.Abs(CurrentSpeed) >= minimumSteerSpeed)
        {
            carRigidbody.MoveRotation(carRigidbody.rotation * Quaternion.Euler(0f, yaw, 0f));
        }
    }

    private void OnValidate()
    {
        maxForwardSpeed = Mathf.Max(0.01f, maxForwardSpeed);
        maxReverseSpeed = Mathf.Max(0.01f, maxReverseSpeed);
        acceleration = Mathf.Max(0f, acceleration);
        braking = Mathf.Max(0f, braking);
        coastDrag = Mathf.Max(0f, coastDrag);
        turnSpeed = Mathf.Max(0f, turnSpeed);
        maxSteerAngle = Mathf.Clamp(maxSteerAngle, 0f, 90f);
        wheelBase = Mathf.Max(0.01f, wheelBase);
        steeringResponse = Mathf.Max(0.01f, steeringResponse);
        minimumSteerSpeed = Mathf.Max(0f, minimumSteerSpeed);
        highSpeedSteerFactor = Mathf.Clamp01(highSpeedSteerFactor);
        lateralGrip = Mathf.Max(0f, lateralGrip);
    }
}
