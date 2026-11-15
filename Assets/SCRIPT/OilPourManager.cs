using UnityEngine;

/// <summary>
/// Bridges AR_OilInteraction.cs state machine to:
///   Phase 1 — OilParticleConfigurator (pour stream)
///   Phase 2 — OilFillController       (shader tank fill)
/// Attach to AR_OilInteraction_Manager GameObject.
/// </summary>
public class OilPourManager : MonoBehaviour
{
    [Header("Phase 1 — Pour Stream")]
    public OilParticleConfigurator particleConfigurator;

    [Header("Phase 2 — Tank Fill")]
    public OilFillController fillController;

    [Tooltip("Seconds after PourComplete before fill animation starts. " +
             "Should be >= particle lifetime so stream visually finishes first.")]
    public float fillStartDelay = 0.8f;

    private int _currentOilType = 0;

    // Called from AR_OilInteraction.cs — state → Pouring
    public void OnPourStart(int oilTypeIndex, GameObject activeBottleInstance)
    {
        _currentOilType = oilTypeIndex;

        Transform nozzle = activeBottleInstance.transform.Find("NozzlePoint");
        if (nozzle == null)
        {
            Debug.LogWarning("[OilPourManager] NozzlePoint not found — using bottle root.");
            nozzle = activeBottleInstance.transform;
        }

        particleConfigurator.StartOilPour(oilTypeIndex, nozzle);
    }

    // Called from AR_OilInteraction.cs — state → PourComplete
    public void OnPourComplete()
    {
        particleConfigurator.StopOilPour();
        // Delay fill start so particles finish visually before fill appears
        Invoke(nameof(StartFillPhase), fillStartDelay);
    }

    // Called from AR_OilInteraction.cs — state → PourFailed
    public void OnPourFailed()
    {
        CancelInvoke(nameof(StartFillPhase)); // prevent fill if called after delay starts
        particleConfigurator.AbortOilPour();
        // Do NOT call fillController — pour failed, no oil entered engine
    }

    // Called from AR_OilInteraction.cs — AutoResetEngine() / Reset
    public void OnReset()
    {
        CancelInvoke(nameof(StartFillPhase));
        particleConfigurator.AbortOilPour();
        fillController.ResetFill();
        _currentOilType = 0;
    }

    private void StartFillPhase()
    {
        fillController.BeginFilling(_currentOilType);
    }
}