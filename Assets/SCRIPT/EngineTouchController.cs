using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Touch/drag throttle controller for mobile AR applications
/// Can be used with UI sliders or touch gestures
/// </summary>
public class EngineTouchController : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("=== REFERENCES ===")]
    [SerializeField] private AdvancedEngineAnimator engineAnimator;

    [Header("=== CONTROL MODE ===")]
    [SerializeField] private ControlMode controlMode = ControlMode.VerticalDrag;

    [Header("=== DRAG SETTINGS ===")]
    [Tooltip("How much drag distance equals full throttle")]
    [SerializeField] private float dragSensitivity = 200f;

    [Tooltip("Return to idle when touch is released")]
    [SerializeField] private bool returnToIdleOnRelease = true;

    [Tooltip("Speed of returning to idle")]
    [SerializeField] private float returnSpeed = 2f;

    [Header("=== TAP SETTINGS ===")]
    [Tooltip("Throttle increase per tap")]
    [SerializeField] private float throttlePerTap = 0.25f;

    [Tooltip("Time window for tap before it's considered a hold")]
    [SerializeField] private float tapThreshold = 0.2f;

    [Header("=== HOLD SETTINGS ===")]
    [Tooltip("How fast throttle increases when holding")]
    [SerializeField] private float holdAcceleration = 1f;

    [Header("=== VISUAL FEEDBACK ===")]
    [SerializeField] private RectTransform thumbVisual;
    [SerializeField] private float thumbTravelDistance = 100f;

    public enum ControlMode
    {
        VerticalDrag,      // Drag up/down to control throttle
        HorizontalDrag,    // Drag left/right to control throttle
        TapToIncrease,     // Tap to increase, auto-decrease
        HoldToAccelerate   // Hold to accelerate, release to decelerate
    }

    private bool isPointerDown = false;
    private float pointerDownTime;
    private Vector2 dragStartPosition;
    private float dragStartThrottle;
    private Vector3 thumbStartPosition;

    private void Awake()
    {
        if (thumbVisual != null)
        {
            thumbStartPosition = thumbVisual.localPosition;
        }
    }

    private void Update()
    {
        if (engineAnimator == null) return;

        // Handle hold mode
        if (controlMode == ControlMode.HoldToAccelerate && isPointerDown)
        {
            float holdDuration = Time.time - pointerDownTime;
            if (holdDuration > tapThreshold)
            {
                float newThrottle = engineAnimator.GetThrottle() + holdAcceleration * Time.deltaTime;
                engineAnimator.SetThrottle(newThrottle);
            }
        }

        // Auto-return to idle
        if (returnToIdleOnRelease && !isPointerDown)
        {
            if (controlMode == ControlMode.TapToIncrease || controlMode == ControlMode.HoldToAccelerate)
            {
                float currentThrottle = engineAnimator.GetThrottle();
                if (currentThrottle > 0)
                {
                    engineAnimator.SetThrottle(currentThrottle - returnSpeed * Time.deltaTime);
                }
            }
        }

        // Update thumb visual
        UpdateThumbVisual();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isPointerDown = true;
        pointerDownTime = Time.time;
        dragStartPosition = eventData.position;

        if (engineAnimator != null)
        {
            dragStartThrottle = engineAnimator.GetThrottle();
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerDown = false;

        // Handle tap in TapToIncrease mode
        if (controlMode == ControlMode.TapToIncrease)
        {
            float tapDuration = Time.time - pointerDownTime;
            if (tapDuration < tapThreshold && engineAnimator != null)
            {
                float newThrottle = engineAnimator.GetThrottle() + throttlePerTap;
                engineAnimator.SetThrottle(newThrottle);
            }
        }

        // Return thumb to start
        if (returnToIdleOnRelease &&
            (controlMode == ControlMode.VerticalDrag || controlMode == ControlMode.HorizontalDrag))
        {
            if (engineAnimator != null)
            {
                engineAnimator.SetThrottle(0f);
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (engineAnimator == null) return;

        if (controlMode == ControlMode.VerticalDrag || controlMode == ControlMode.HorizontalDrag)
        {
            Vector2 delta = eventData.position - dragStartPosition;
            float dragAmount;

            if (controlMode == ControlMode.VerticalDrag)
            {
                dragAmount = delta.y / dragSensitivity;
            }
            else
            {
                dragAmount = delta.x / dragSensitivity;
            }

            float newThrottle = dragStartThrottle + dragAmount;
            engineAnimator.SetThrottle(newThrottle);
        }
    }

    private void UpdateThumbVisual()
    {
        if (thumbVisual == null || engineAnimator == null) return;

        float throttle = engineAnimator.GetThrottle();
        Vector3 offset = Vector3.zero;

        switch (controlMode)
        {
            case ControlMode.VerticalDrag:
            case ControlMode.TapToIncrease:
            case ControlMode.HoldToAccelerate:
                offset = new Vector3(0f, throttle * thumbTravelDistance, 0f);
                break;

            case ControlMode.HorizontalDrag:
                offset = new Vector3(throttle * thumbTravelDistance, 0f, 0f);
                break;
        }

        thumbVisual.localPosition = thumbStartPosition + offset;
    }

    /// <summary>
    /// Set the engine animator at runtime
    /// </summary>
    public void SetEngineAnimator(AdvancedEngineAnimator animator)
    {
        engineAnimator = animator;
    }

    /// <summary>
    /// Find and link to engine animator automatically
    /// </summary>
    public void AutoLinkEngine()
    {
        engineAnimator = FindFirstObjectByType<AdvancedEngineAnimator>();

        if (engineAnimator != null)
        {
            Debug.Log($"Auto-linked to engine: {engineAnimator.gameObject.name}");
        }
        else
        {
            Debug.LogWarning("No AdvancedEngineAnimator found in scene");
        }
    }
}
