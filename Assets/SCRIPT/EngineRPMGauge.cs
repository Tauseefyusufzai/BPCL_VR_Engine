using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI component to display engine RPM as a gauge with optional digital readout
/// </summary>
public class EngineRPMGauge : MonoBehaviour
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private AdvancedEngineAnimator engineAnimator;

    [Header("=== NEEDLE GAUGE ===")]
    [Tooltip("The needle transform that rotates to show RPM")]
    [SerializeField] private RectTransform needle;

    [Tooltip("Rotation angle at minimum RPM (usually pointing left)")]
    [SerializeField] private float minAngle = 135f;

    [Tooltip("Rotation angle at maximum RPM (usually pointing right)")]
    [SerializeField] private float maxAngle = -135f;

    [Tooltip("How quickly the needle moves (higher = more responsive)")]
    [SerializeField] private float needleSmoothSpeed = 5f;

    [Header("=== DIGITAL DISPLAY ===")]
    [SerializeField] private Text rpmText;
    [SerializeField] private Text throttleText;
    [SerializeField] private string rpmFormat = "{0:0} RPM";
    [SerializeField] private string throttleFormat = "THR: {0:0}%";

    [Header("=== REDLINE WARNING ===")]
    [SerializeField] private Image redlineIndicator;
    [SerializeField] private Color normalColor = Color.green;
    [SerializeField] private Color warningColor = Color.yellow;
    [SerializeField] private Color redlineColor = Color.red;

    [Tooltip("RPM percentage at which warning color starts")]
    [Range(0f, 1f)]
    [SerializeField] private float warningThreshold = 0.75f;

    [Tooltip("RPM percentage at which redline color starts")]
    [Range(0f, 1f)]
    [SerializeField] private float redlineThreshold = 0.9f;

    [Header("=== EFFECTS ===")]
    [SerializeField] private bool enableNeedleBounce = true;
    [SerializeField] private float bounceIntensity = 2f;

    private float currentNeedleAngle;
    private float targetNeedleAngle;

    private void Update()
    {
        if (engineAnimator == null) return;

        float normalizedRPM = engineAnimator.GetNormalizedRPM();
        float currentRPM = engineAnimator.GetCurrentRPM();
        float throttle = engineAnimator.GetThrottle();

        UpdateNeedle(normalizedRPM, currentRPM);
        UpdateDigitalDisplay(currentRPM, throttle);
        UpdateRedlineIndicator(normalizedRPM);
    }

    private void UpdateNeedle(float normalizedRPM, float currentRPM)
    {
        if (needle == null) return;

        // Calculate target angle
        targetNeedleAngle = Mathf.Lerp(minAngle, maxAngle, normalizedRPM);

        // Add bounce effect at high RPM
        if (enableNeedleBounce && normalizedRPM > warningThreshold)
        {
            float bounceAmount = (normalizedRPM - warningThreshold) / (1f - warningThreshold);
            float bounce = Mathf.Sin(Time.time * 30f) * bounceIntensity * bounceAmount;
            targetNeedleAngle += bounce;
        }

        // Smooth interpolation
        currentNeedleAngle = Mathf.Lerp(currentNeedleAngle, targetNeedleAngle, Time.deltaTime * needleSmoothSpeed);

        // Apply rotation
        needle.localRotation = Quaternion.Euler(0f, 0f, currentNeedleAngle);
    }

    private void UpdateDigitalDisplay(float currentRPM, float throttle)
    {
        if (rpmText != null)
        {
            rpmText.text = string.Format(rpmFormat, currentRPM);
        }

        if (throttleText != null)
        {
            throttleText.text = string.Format(throttleFormat, throttle * 100f);
        }
    }

    private void UpdateRedlineIndicator(float normalizedRPM)
    {
        if (redlineIndicator == null) return;

        Color targetColor;

        if (normalizedRPM >= redlineThreshold)
        {
            // Blink at redline
            float blink = Mathf.PingPong(Time.time * 10f, 1f);
            targetColor = Color.Lerp(redlineColor, Color.white, blink * 0.3f);
        }
        else if (normalizedRPM >= warningThreshold)
        {
            float t = (normalizedRPM - warningThreshold) / (redlineThreshold - warningThreshold);
            targetColor = Color.Lerp(warningColor, redlineColor, t);
        }
        else
        {
            targetColor = normalColor;
        }

        redlineIndicator.color = targetColor;
    }

    /// <summary>
    /// Manually assign the engine animator at runtime
    /// </summary>
    public void SetEngineAnimator(AdvancedEngineAnimator animator)
    {
        engineAnimator = animator;
    }
}
