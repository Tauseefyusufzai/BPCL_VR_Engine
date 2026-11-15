using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Hooks into the existing AR_OilInteraction state machine to drive
/// the VFX Graph oil pour particle system.
/// Attach this component to AR_OilInteraction_Manager GameObject.
/// </summary>
public class OilPourVFXController : MonoBehaviour
{
    [Header("VFX Asset")]
    public VisualEffect oilVFX;                  // drag OilPourVFX prefab here

    [Header("Scene References")]
    public Transform engineOilHoleTransform;     // same ref as AR_OilInteraction.cs

    [Header("BPCL Oil Settings")]
    public Color bpclColor     = new Color(0.784f, 0.659f, 0.294f); // #C8A84B
    public float bpclSize      = 0.010f;
    public float bpclSpawnRate = 200f;
    public float bpclViscosity = 0.22f;

    [Header("Local Oil Settings")]
    public Color localColor    = new Color(0.361f, 0.227f, 0.063f); // #5C3A10
    public float localSize     = 0.018f;
    public float localSpawnRate= 160f;
    public float localViscosity= 0.38f;

    [Header("Transition")]
    [Tooltip("Optional: assign FillController to trigger shader fill after VFX settles")]
    public MonoBehaviour shaderFillController;   // your Approach 1 fill controller
    public float shaderFillDelay = 0.8f;         // seconds after pour stops

    // Internal
    private bool  _isPouring   = false;
    private Transform _currentNozzle = null;

    // -------------------------------------------------------
    // Called by AR_OilInteraction.cs when pour begins.
    // Pass the selected OilType and the active bottle's nozzle.
    // -------------------------------------------------------
    public void OnPourStart(int oilTypeIndex, Transform nozzlePoint)
    {
        _isPouring      = true;
        _currentNozzle  = nozzlePoint;

        // Apply oil-type-specific properties
        if (oilTypeIndex == 0)  // BPCL
            ApplyOilSettings(bpclColor, bpclSize, bpclSpawnRate, bpclViscosity);
        else                    // Local Mineral
            ApplyOilSettings(localColor, localSize, localSpawnRate, localViscosity);

        // Trigger the single burst splash + start continuous stream
        if(oilVFX != null)
        {
            if (_currentNozzle != null) oilVFX.SetVector3("_NozzlePosition", _currentNozzle.position);
            oilVFX.Play();
            oilVFX.SendEvent("OnPourStart");
        }
    }

    // -------------------------------------------------------
    // Called by AR_OilInteraction.cs when PourComplete fires.
    // -------------------------------------------------------
    public void OnPourComplete()
    {
        _isPouring = false;
        if(oilVFX != null) oilVFX.SetFloat("_SpawnRate", 0f);  // stop new particles; live ones finish naturally

        // Trigger shader fill after a short delay (particles settle visually)
        if (shaderFillController != null)
            Invoke(nameof(TriggerShaderFill), shaderFillDelay);
    }

    // -------------------------------------------------------
    // Called by AR_OilInteraction.cs when PourFailed fires.
    // -------------------------------------------------------
    public void OnPourFailed()
    {
        _isPouring = false;
        if(oilVFX != null)
        {
            oilVFX.SetFloat("_SpawnRate", 0f);
            oilVFX.Stop();  // hard stop — pour failed, don't show fill
        }
    }

    // -------------------------------------------------------
    // Called by AR_OilInteraction.cs on session Reset.
    // -------------------------------------------------------
    public void OnReset()
    {
        _isPouring = false;
        _currentNozzle = null;
        if(oilVFX != null)
        {
            oilVFX.Stop();
            oilVFX.Reinit();
        }
    }

    void Update()
    {
        if (!_isPouring || _currentNozzle == null || oilVFX == null) return;

        // Track bottle nozzle position every frame (bottle moves during pour animation)
        oilVFX.SetVector3("_NozzlePosition", _currentNozzle.position);
    }

    // -------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------
    private void ApplyOilSettings(Color color, float size, float rate, float viscosity)
    {
        if(oilVFX == null) return;
        
        oilVFX.SetVector4("_OilColor",    color);
        oilVFX.SetFloat("_ParticleSize",  size);
        oilVFX.SetFloat("_SpawnRate",     rate);
        oilVFX.SetFloat("_OilViscosity",  viscosity);
    }

    private void TriggerShaderFill()
    {
        // Calls BeginFilling() on your Approach 1 FillController script
        if(shaderFillController != null)
            shaderFillController.SendMessage("BeginFilling", SendMessageOptions.DontRequireReceiver);
    }
}