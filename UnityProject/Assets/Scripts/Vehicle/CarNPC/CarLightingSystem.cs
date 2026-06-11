using UnityEngine;

public class CarLightingSystem : MonoBehaviour
{
    public KeyCode leftTurnKey = KeyCode.Q;
    public KeyCode rightTurnKey = KeyCode.E;
    public KeyCode hazardKey = KeyCode.Z;
    public KeyCode brakeKey = KeyCode.S;
    public KeyCode steerLeftKey = KeyCode.A;
    public KeyCode steerRightKey = KeyCode.D;
    public float blinkInterval = 0.35f;
    public float turnSignalIntensity = 4f;
    public Color turnSignalColor = new Color(1f, 0.55f, 0.05f, 1f);
    public float autoCancelTurnAngle = 35f;
    public float tailLightIntensity = 0.8f;
    public float brakeLightIntensity = 4f;
    public Color tailLightColor = Color.red;

    [Header("Optional Lights")]
    public Light[] leftTurnLights = new Light[0];
    public Light[] rightTurnLights = new Light[0];
    public Light[] brakeLights = new Light[0];

    public bool LeftTurnActive { get; private set; }
    public bool RightTurnActive { get; private set; }
    public bool HazardActive { get; private set; }
    public bool BrakeActive { get; private set; }

    private float blinkTimer;
    private bool blinkOn;

    private void Update()
    {
        if (Input.GetKeyDown(leftTurnKey))
        {
            SetTurnSignals(!LeftTurnActive, false);
        }

        if (Input.GetKeyDown(rightTurnKey))
        {
            SetTurnSignals(false, !RightTurnActive);
        }

        if (Input.GetKeyDown(hazardKey))
        {
            SetHazard(!HazardActive);
        }

        BrakeActive = Input.GetKey(brakeKey);
        UpdateBlink();
        ApplyLights();
    }

    public void SetTurnSignals(bool left, bool right)
    {
        LeftTurnActive = left;
        RightTurnActive = right;
        HazardActive = false;
    }

    public void SetHazard(bool active)
    {
        HazardActive = active;
        if (active)
        {
            LeftTurnActive = false;
            RightTurnActive = false;
        }
    }

    public void SetBrake(bool active)
    {
        BrakeActive = active;
        ApplyLights();
    }

    private void UpdateBlink()
    {
        blinkTimer += Time.deltaTime;
        if (blinkTimer >= blinkInterval)
        {
            blinkTimer = 0f;
            blinkOn = !blinkOn;
        }
    }

    private void ApplyLights()
    {
        bool leftOn = blinkOn && (LeftTurnActive || HazardActive);
        bool rightOn = blinkOn && (RightTurnActive || HazardActive);

        ApplyLightSet(leftTurnLights, leftOn, turnSignalColor, turnSignalIntensity);
        ApplyLightSet(rightTurnLights, rightOn, turnSignalColor, turnSignalIntensity);
        ApplyLightSet(brakeLights, BrakeActive, tailLightColor, BrakeActive ? brakeLightIntensity : tailLightIntensity);
    }

    private void ApplyLightSet(Light[] lights, bool enabled, Color color, float intensity)
    {
        if (lights == null)
        {
            return;
        }

        foreach (Light light in lights)
        {
            if (light == null)
            {
                continue;
            }

            light.enabled = enabled;
            light.color = color;
            light.intensity = intensity;
        }
    }

    private void OnValidate()
    {
        blinkInterval = Mathf.Max(0.01f, blinkInterval);
        turnSignalIntensity = Mathf.Max(0f, turnSignalIntensity);
        autoCancelTurnAngle = Mathf.Max(0f, autoCancelTurnAngle);
        tailLightIntensity = Mathf.Max(0f, tailLightIntensity);
        brakeLightIntensity = Mathf.Max(0f, brakeLightIntensity);
    }
}
