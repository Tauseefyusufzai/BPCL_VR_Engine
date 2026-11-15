using UnityEngine;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Advanced Engine Animator - Mathematically animates pistons, connecting rods, and crankshaft
/// without requiring pre-made animations. Supports various engine configurations.
/// </summary>
public class AdvancedEngineAnimator : MonoBehaviour
{
    #region Enums

    public enum EngineType
    {
        Inline4,
        Inline6,
        V6,
        V8,
        V12,
        Boxer4,
        Boxer6,
        Single,
        Custom
    }

    public enum RotationAxis
    {
        X,
        Y,
        Z
    }

    #endregion

    #region Serialized Fields

    [Header("=== ENGINE CONFIGURATION ===")]
    [SerializeField] private EngineType engineType = EngineType.Inline4;
    [SerializeField] private RotationAxis crankshaftRotationAxis = RotationAxis.Z;
    [SerializeField] private bool reverseRotation = false;

    [Header("=== COMPONENT REFERENCES ===")]
    [SerializeField] private Transform crankshaft;
    [SerializeField] private CylinderSetup[] cylinders;

    [Header("=== DIMENSIONS (in local units) ===")]
    [Tooltip("Distance from crankshaft center to crank pin (half of piston stroke)")]
    [SerializeField] private float crankRadius = 0.5f;

    [Tooltip("Length of the connecting rod from crank pin to piston pin")]
    [SerializeField] private float connectingRodLength = 1.5f;

    [Header("=== RPM CONTROL ===")]
    [SerializeField] private float idleRPM = 800f;
    [SerializeField] private float maxRPM = 8000f;
    [SerializeField] private float currentRPM = 800f;

    [Tooltip("How quickly RPM changes when throttle is applied")]
    [SerializeField] private float rpmAcceleration = 2000f;

    [Tooltip("How quickly RPM drops when throttle is released")]
    [SerializeField] private float rpmDeceleration = 1500f;

    [Header("=== INPUT ===")]
    [Range(0f, 1f)]
    [SerializeField] private float throttle = 0f;

    [Tooltip("Enable keyboard input (W/S or Up/Down arrows)")]
    [SerializeField] private bool useKeyboardInput = true;

    [Header("=== VIBRATION EFFECTS ===")]
    [SerializeField] private bool enableVibration = true;
    [SerializeField] private float vibrationIntensity = 0.001f;
    [SerializeField] private Transform vibrationTarget;

    [Header("=== AUDIO ===")]
    [SerializeField] private AudioSource engineAudioSource;
    [SerializeField] private float minPitch = 0.5f;
    [SerializeField] private float maxPitch = 2.0f;

    [Header("=== DEBUG ===")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private Color gizmoColor = Color.yellow;

    #endregion

    #region Private Fields

    private float currentAngle = 0f;
    private float[] phaseOffsets;
    private float[] bankAngles;
    private Vector3 originalVibrationPosition;

    #endregion

    #region Nested Classes

    [Serializable]
    public class CylinderSetup
    {
        [Tooltip("The piston transform")]
        public Transform piston;

        [Tooltip("The connecting rod transform")]
        public Transform connectingRod;

        [Tooltip("The pivot point of the connecting rod at the crankshaft end")]
        public Transform connectingRodPivot;

        [Tooltip("Custom phase offset in degrees (only used if engine type is Custom)")]
        public float customPhaseOffset = 0f;

        [Tooltip("Bank angle for V-engines (0 for inline/boxer)")]
        public float customBankAngle = 0f;

        [HideInInspector] public Vector3 pistonStartPosition;
        [HideInInspector] public Quaternion connectingRodStartRotation;
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        InitializePhaseOffsets();
        CacheInitialPositions();

        if (vibrationTarget != null)
        {
            originalVibrationPosition = vibrationTarget.localPosition;
        }
    }

    private void Start()
    {
        // Debug info on start
        Debug.Log($"[EngineAnimator] Started on '{gameObject.name}'");
        Debug.Log($"[EngineAnimator] Crankshaft: {(crankshaft != null ? crankshaft.name : "NOT ASSIGNED")}");
        Debug.Log($"[EngineAnimator] Cylinders: {(cylinders != null ? cylinders.Length : 0)}");
        Debug.Log($"[EngineAnimator] Crank Radius: {crankRadius}, Rod Length: {connectingRodLength}");

        if (cylinders != null)
        {
            for (int i = 0; i < cylinders.Length; i++)
            {
                var c = cylinders[i];
                if (c != null)
                {
                    Debug.Log($"[EngineAnimator] Cylinder {i}: Piston={c.piston?.name ?? "NULL"}, Rod={c.connectingRod?.name ?? "NULL"}");
                }
                else
                {
                    Debug.Log($"[EngineAnimator] Cylinder {i}: NULL");
                }
            }
        }

        if (crankshaft == null)
        {
            Debug.LogWarning("[EngineAnimator] No crankshaft assigned! The engine won't animate.");
        }
        if (cylinders == null || cylinders.Length == 0)
        {
            Debug.LogWarning("[EngineAnimator] No cylinders/pistons assigned! The engine won't animate.");
        }
    }

    private void Update()
    {
        HandleInput();
        UpdateRPM();
        AnimateEngine();
        ApplyVibration();
        UpdateAudio();
    }

    private void OnValidate()
    {
        InitializePhaseOffsets();

        // Ensure crank radius is less than connecting rod length
        if (crankRadius >= connectingRodLength)
        {
            crankRadius = connectingRodLength * 0.3f;
        }
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || crankshaft == null) return;

        Gizmos.color = gizmoColor;

        // Draw crankshaft center
        Gizmos.DrawWireSphere(crankshaft.position, crankRadius * 0.2f);

        // Draw crank circle
        DrawGizmoCircle(crankshaft.position, crankRadius, 32);

        // Draw cylinder positions
        if (cylinders != null)
        {
            foreach (var cylinder in cylinders)
            {
                if (cylinder?.piston != null)
                {
                    Gizmos.color = Color.red;
                    Gizmos.DrawWireSphere(cylinder.piston.position, crankRadius * 0.15f);

                    if (cylinder.connectingRod != null)
                    {
                        Gizmos.color = Color.blue;
                        Gizmos.DrawLine(cylinder.piston.position,
                            cylinder.connectingRodPivot != null
                                ? cylinder.connectingRodPivot.position
                                : cylinder.connectingRod.position);
                    }
                }
            }
        }
    }

    #endregion

    #region Initialization

    private void InitializePhaseOffsets()
    {
        switch (engineType)
        {
            case EngineType.Single:
                phaseOffsets = new float[] { 0f };
                bankAngles = new float[] { 0f };
                break;

            case EngineType.Inline4:
                // Firing order: 1-3-4-2
                phaseOffsets = new float[] { 0f, 180f, 180f, 0f };
                bankAngles = new float[] { 0f, 0f, 0f, 0f };
                break;

            case EngineType.Inline6:
                // Firing order: 1-5-3-6-2-4
                phaseOffsets = new float[] { 0f, 240f, 120f, 120f, 240f, 0f };
                bankAngles = new float[] { 0f, 0f, 0f, 0f, 0f, 0f };
                break;

            case EngineType.V6:
                // 60-degree V6
                phaseOffsets = new float[] { 0f, 120f, 240f, 30f, 150f, 270f };
                bankAngles = new float[] { 30f, 30f, 30f, -30f, -30f, -30f };
                break;

            case EngineType.V8:
                // 90-degree V8, cross-plane crankshaft
                phaseOffsets = new float[] { 0f, 90f, 270f, 180f, 180f, 270f, 90f, 0f };
                bankAngles = new float[] { 45f, 45f, 45f, 45f, -45f, -45f, -45f, -45f };
                break;

            case EngineType.V12:
                // 60-degree V12
                phaseOffsets = new float[] { 0f, 120f, 240f, 60f, 180f, 300f,
                                             30f, 150f, 270f, 90f, 210f, 330f };
                bankAngles = new float[] { 30f, 30f, 30f, 30f, 30f, 30f,
                                          -30f, -30f, -30f, -30f, -30f, -30f };
                break;

            case EngineType.Boxer4:
                // Horizontally opposed 4-cylinder
                phaseOffsets = new float[] { 0f, 180f, 180f, 0f };
                bankAngles = new float[] { 90f, 90f, -90f, -90f };
                break;

            case EngineType.Boxer6:
                // Horizontally opposed 6-cylinder
                phaseOffsets = new float[] { 0f, 120f, 240f, 60f, 180f, 300f };
                bankAngles = new float[] { 90f, 90f, 90f, -90f, -90f, -90f };
                break;

            case EngineType.Custom:
                // Use custom values from cylinder setup
                if (cylinders != null)
                {
                    phaseOffsets = new float[cylinders.Length];
                    bankAngles = new float[cylinders.Length];
                    for (int i = 0; i < cylinders.Length; i++)
                    {
                        phaseOffsets[i] = cylinders[i].customPhaseOffset;
                        bankAngles[i] = cylinders[i].customBankAngle;
                    }
                }
                break;
        }
    }

    private void CacheInitialPositions()
    {
        if (cylinders == null) return;

        foreach (var cylinder in cylinders)
        {
            if (cylinder.piston != null)
            {
                cylinder.pistonStartPosition = cylinder.piston.localPosition;
            }
            if (cylinder.connectingRod != null)
            {
                cylinder.connectingRodStartRotation = cylinder.connectingRod.localRotation;
            }
        }
    }

    #endregion

    #region Input Handling

    private void HandleInput()
    {
        if (!useKeyboardInput) return;

#if ENABLE_INPUT_SYSTEM
        // New Input System
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
        {
            throttle = Mathf.Min(throttle + Time.deltaTime, 1f);
        }
        else if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
        {
            throttle = Mathf.Max(throttle - Time.deltaTime, 0f);
        }
        else
        {
            // Gradually release throttle
            throttle = Mathf.Max(throttle - Time.deltaTime * 0.5f, 0f);
        }
#else
        // Legacy Input System
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            throttle = Mathf.Min(throttle + Time.deltaTime, 1f);
        }
        else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            throttle = Mathf.Max(throttle - Time.deltaTime, 0f);
        }
        else
        {
            // Gradually release throttle
            throttle = Mathf.Max(throttle - Time.deltaTime * 0.5f, 0f);
        }
#endif
    }

    private void UpdateRPM()
    {
        float targetRPM = Mathf.Lerp(idleRPM, maxRPM, throttle);

        if (currentRPM < targetRPM)
        {
            currentRPM = Mathf.Min(currentRPM + rpmAcceleration * Time.deltaTime, targetRPM);
        }
        else
        {
            currentRPM = Mathf.Max(currentRPM - rpmDeceleration * Time.deltaTime, targetRPM);
        }
    }

    #endregion

    #region Animation

    private void AnimateEngine()
    {
        // Convert RPM to degrees per second
        float degreesPerSecond = (currentRPM / 60f) * 360f;

        // Update angle
        float direction = reverseRotation ? -1f : 1f;
        currentAngle += degreesPerSecond * direction * Time.deltaTime;
        currentAngle %= 360f;

        // Rotate crankshaft
        AnimateCrankshaft();

        // Animate each cylinder
        AnimateCylinders();
    }

    private void AnimateCrankshaft()
    {
        if (crankshaft == null) return;

        Vector3 rotation = Vector3.zero;
        switch (crankshaftRotationAxis)
        {
            case RotationAxis.X:
                rotation = new Vector3(currentAngle, 0f, 0f);
                break;
            case RotationAxis.Y:
                rotation = new Vector3(0f, currentAngle, 0f);
                break;
            case RotationAxis.Z:
                rotation = new Vector3(0f, 0f, currentAngle);
                break;
        }

        crankshaft.localRotation = Quaternion.Euler(rotation);
    }

    private void AnimateCylinders()
    {
        if (cylinders == null) return;

        for (int i = 0; i < cylinders.Length; i++)
        {
            if (cylinders[i].piston == null) continue;

            float phase = i < phaseOffsets.Length ? phaseOffsets[i] : 0f;
            float bank = i < bankAngles.Length ? bankAngles[i] : 0f;

            // Calculate crank pin position
            float angleRad = (currentAngle + phase) * Mathf.Deg2Rad;

            // Piston position using exact kinematics equation:
            // y = r * cos(θ) + sqrt(l² - r² * sin²(θ))
            // Where r = crank radius, l = connecting rod length, θ = crank angle

            float sinTheta = Mathf.Sin(angleRad);
            float cosTheta = Mathf.Cos(angleRad);

            float rodProjection = Mathf.Sqrt(
                connectingRodLength * connectingRodLength -
                crankRadius * crankRadius * sinTheta * sinTheta
            );

            // Piston displacement from TDC (Top Dead Center)
            float pistonY = crankRadius * cosTheta + rodProjection;

            // Normalize to oscillate around the start position
            // TDC is at (crankRadius + connectingRodLength), BDC is at (connectingRodLength - crankRadius)
            float centerOffset = connectingRodLength;
            float displacement = pistonY - centerOffset;

            // Apply piston position
            Vector3 pistonPos = cylinders[i].pistonStartPosition;

            // Apply bank angle offset for V/boxer engines
            if (Mathf.Abs(bank) > 0.01f)
            {
                float bankRad = bank * Mathf.Deg2Rad;
                pistonPos.x = cylinders[i].pistonStartPosition.x + displacement * Mathf.Sin(bankRad);
                pistonPos.y = cylinders[i].pistonStartPosition.y + displacement * Mathf.Cos(bankRad);
            }
            else
            {
                pistonPos.y = cylinders[i].pistonStartPosition.y + displacement;
            }

            cylinders[i].piston.localPosition = pistonPos;

            // Animate connecting rod
            AnimateConnectingRod(cylinders[i], angleRad, bank);
        }
    }

    private void AnimateConnectingRod(CylinderSetup cylinder, float crankAngleRad, float bankAngle)
    {
        if (cylinder.connectingRod == null) return;

        // Calculate connecting rod angle
        // φ = arcsin((r/l) * sin(θ))
        float sinTheta = Mathf.Sin(crankAngleRad);
        float rodAngle = Mathf.Asin((crankRadius / connectingRodLength) * sinTheta);
        float rodAngleDegrees = rodAngle * Mathf.Rad2Deg;

        // Apply rotation to connecting rod
        Vector3 rodRotation = Vector3.zero;

        // The connecting rod rotates opposite to the visual tilt
        switch (crankshaftRotationAxis)
        {
            case RotationAxis.X:
                rodRotation = new Vector3(0f, rodAngleDegrees, bankAngle);
                break;
            case RotationAxis.Y:
                rodRotation = new Vector3(rodAngleDegrees, 0f, bankAngle);
                break;
            case RotationAxis.Z:
                rodRotation = new Vector3(bankAngle, 0f, rodAngleDegrees);
                break;
        }

        cylinder.connectingRod.localRotation = cylinder.connectingRodStartRotation * Quaternion.Euler(rodRotation);

        // Update connecting rod pivot position if specified
        if (cylinder.connectingRodPivot != null)
        {
            // Position the pivot at the crank pin position
            float cosTheta = Mathf.Cos(crankAngleRad);
            Vector3 crankPinOffset = Vector3.zero;

            switch (crankshaftRotationAxis)
            {
                case RotationAxis.X:
                    crankPinOffset = new Vector3(0f, crankRadius * cosTheta, crankRadius * sinTheta);
                    break;
                case RotationAxis.Y:
                    crankPinOffset = new Vector3(crankRadius * sinTheta, 0f, crankRadius * cosTheta);
                    break;
                case RotationAxis.Z:
                    crankPinOffset = new Vector3(crankRadius * sinTheta, crankRadius * cosTheta, 0f);
                    break;
            }

            // Apply bank angle
            if (Mathf.Abs(bankAngle) > 0.01f)
            {
                crankPinOffset = Quaternion.Euler(0f, 0f, bankAngle) * crankPinOffset;
            }

            cylinder.connectingRodPivot.localPosition = crankPinOffset;
        }
    }

    #endregion

    #region Effects

    private void ApplyVibration()
    {
        if (!enableVibration || vibrationTarget == null) return;

        // Vibration increases with RPM
        float rpmFactor = currentRPM / maxRPM;
        float intensity = vibrationIntensity * rpmFactor;

        // Create pseudo-random vibration based on crank angle
        float vibX = Mathf.Sin(currentAngle * 0.1f) * intensity;
        float vibY = Mathf.Cos(currentAngle * 0.15f) * intensity;
        float vibZ = Mathf.Sin(currentAngle * 0.12f) * intensity * 0.5f;

        vibrationTarget.localPosition = originalVibrationPosition + new Vector3(vibX, vibY, vibZ);
    }

    private void UpdateAudio()
    {
        if (engineAudioSource == null) return;

        // Map RPM to pitch
        float rpmNormalized = (currentRPM - idleRPM) / (maxRPM - idleRPM);
        engineAudioSource.pitch = Mathf.Lerp(minPitch, maxPitch, rpmNormalized);

        // Volume based on throttle
        engineAudioSource.volume = Mathf.Lerp(0.3f, 1f, throttle);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Set the throttle value (0-1)
    /// </summary>
    public void SetThrottle(float value)
    {
        throttle = Mathf.Clamp01(value);
    }

    /// <summary>
    /// Get current throttle value
    /// </summary>
    public float GetThrottle() => throttle;

    /// <summary>
    /// Get current RPM
    /// </summary>
    public float GetCurrentRPM() => currentRPM;

    /// <summary>
    /// Get RPM as a normalized value (0-1)
    /// </summary>
    public float GetNormalizedRPM() => (currentRPM - idleRPM) / (maxRPM - idleRPM);

    /// <summary>
    /// Instantly set RPM (for external control)
    /// </summary>
    public void SetRPM(float rpm)
    {
        currentRPM = Mathf.Clamp(rpm, idleRPM, maxRPM);
    }

    /// <summary>
    /// Get current crank angle in degrees
    /// </summary>
    public float GetCrankAngle() => currentAngle;

    /// <summary>
    /// Check if engine is at idle
    /// </summary>
    public bool IsIdle() => Mathf.Approximately(currentRPM, idleRPM);

    /// <summary>
    /// Check if engine is at redline
    /// </summary>
    public bool IsAtRedline() => currentRPM >= maxRPM * 0.95f;

    #endregion

    #region Helper Methods

    private void DrawGizmoCircle(Vector3 center, float radius, int segments)
    {
        float angleStep = 360f / segments;
        Vector3 prevPoint = center + GetCirclePoint(0f, radius);

        for (int i = 1; i <= segments; i++)
        {
            Vector3 point = center + GetCirclePoint(angleStep * i, radius);
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }
    }

    private Vector3 GetCirclePoint(float angle, float radius)
    {
        float rad = angle * Mathf.Deg2Rad;
        switch (crankshaftRotationAxis)
        {
            case RotationAxis.X:
                return new Vector3(0f, Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius);
            case RotationAxis.Y:
                return new Vector3(Mathf.Sin(rad) * radius, 0f, Mathf.Cos(rad) * radius);
            case RotationAxis.Z:
            default:
                return new Vector3(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * radius, 0f);
        }
    }

    #endregion
}
