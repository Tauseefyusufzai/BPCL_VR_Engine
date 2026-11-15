using UnityEngine;

/// <summary>
/// Configures existing OilPouringParticles prefab at runtime per oil type.
/// No prefab changes required.
/// </summary>
public class OilParticleConfigurator : MonoBehaviour
{
    [Header("Particle System")]
    public ParticleSystem oilParticleSystem;

    [Header("BPCL — MAK Supreme Synthetic")]
    public Color bpclStartColorMin  = new Color(0.784f, 0.659f, 0.294f, 0.75f);
    public Color bpclStartColorMax  = new Color(0.878f, 0.769f, 0.408f, 0.90f);
    public float bpclSizeMin        = 0.008f;
    public float bpclSizeMax        = 0.012f;
    public float bpclSpeedMin       = 0.35f;
    public float bpclSpeedMax       = 0.55f;
    public float bpclLifetimeMin    = 1.2f;
    public float bpclLifetimeMax    = 2.0f;
    public float bpclEmissionRate   = 700f;
    public float bpclGravity        = 1.1f;

    [Header("Local — Mineral Oil")]
    public Color localStartColorMin = new Color(0.280f, 0.165f, 0.039f, 0.80f);
    public Color localStartColorMax = new Color(0.408f, 0.255f, 0.075f, 0.95f);
    public float localSizeMin       = 0.014f;
    public float localSizeMax       = 0.020f;
    public float localSpeedMin      = 0.25f;
    public float localSpeedMax      = 0.40f;
    public float localLifetimeMin   = 1.5f;
    public float localLifetimeMax   = 2.4f;
    public float localEmissionRate  = 550f;
    public float localGravity       = 1.4f;

    private bool _initialized = false;

    void Awake()
    {
        if (oilParticleSystem == null)
        {
            Debug.LogError("[OilParticleConfigurator] No ParticleSystem assigned.");
            return;
        }
        var main = oilParticleSystem.main;
        main.playOnAwake = false;
        _initialized = true;
        oilParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public void StartOilPour(int oilType, Transform nozzlePoint)
    {
        if (!_initialized) return;
        oilParticleSystem.transform.position = nozzlePoint.position;
        oilParticleSystem.transform.rotation = nozzlePoint.rotation;
        oilParticleSystem.transform.SetParent(nozzlePoint);
        if (oilType == 0) ApplyBPCLSettings(); else ApplyLocalOilSettings();
        oilParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        oilParticleSystem.Play();
    }

    public void StopOilPour()
    {
        if (!_initialized) return;
        var emission = oilParticleSystem.emission;
        emission.rateOverTime = 0f;
        oilParticleSystem.transform.SetParent(null);
    }

    public void AbortOilPour()
    {
        if (!_initialized) return;
        oilParticleSystem.transform.SetParent(null);
        oilParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void ApplyBPCLSettings()
    {
        var main = oilParticleSystem.main;
        main.startColor      = new ParticleSystem.MinMaxGradient(bpclStartColorMin, bpclStartColorMax);
        main.startSize       = new ParticleSystem.MinMaxCurve(bpclSizeMin, bpclSizeMax);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(bpclSpeedMin, bpclSpeedMax);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(bpclLifetimeMin, bpclLifetimeMax);
        main.gravityModifier = bpclGravity;
        main.maxParticles    = 500;
        var emission = oilParticleSystem.emission;
        emission.rateOverTime = bpclEmissionRate;
        ApplyColorOverLifetime(
            new Color(0.878f, 0.769f, 0.408f, 0.90f),
            new Color(0.784f, 0.659f, 0.294f, 0.60f),
            new Color(0.600f, 0.480f, 0.200f, 0.00f));
    }

    private void ApplyLocalOilSettings()
    {
        var main = oilParticleSystem.main;
        main.startColor      = new ParticleSystem.MinMaxGradient(localStartColorMin, localStartColorMax);
        main.startSize       = new ParticleSystem.MinMaxCurve(localSizeMin, localSizeMax);
        main.startSpeed      = new ParticleSystem.MinMaxCurve(localSpeedMin, localSpeedMax);
        main.startLifetime   = new ParticleSystem.MinMaxCurve(localLifetimeMin, localLifetimeMax);
        main.gravityModifier = localGravity;
        main.maxParticles    = 500;
        var emission = oilParticleSystem.emission;
        emission.rateOverTime = localEmissionRate;
        ApplyColorOverLifetime(
            new Color(0.408f, 0.255f, 0.075f, 0.95f),
            new Color(0.280f, 0.165f, 0.039f, 0.75f),
            new Color(0.200f, 0.110f, 0.020f, 0.00f));
    }

    private void ApplyColorOverLifetime(Color startColor, Color midColor, Color endColor)
    {
        var col = oilParticleSystem.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(startColor, 0.0f),
                new GradientColorKey(midColor,   0.5f),
                new GradientColorKey(endColor,   1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(startColor.a, 0.0f),
                new GradientAlphaKey(midColor.a,   0.5f),
                new GradientAlphaKey(endColor.a,   1.0f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }
}