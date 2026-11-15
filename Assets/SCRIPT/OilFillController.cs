using System.Collections;
using UnityEngine;

/// <summary>
/// Animates the OilFill shader _FillLevel on the engine cavity mesh.
/// Attach to EngineOilCavity (the child GameObject with OilFillMaterial).
/// </summary>
public class OilFillController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Renderer that has OilFillMaterial assigned")]
    public Renderer cavityRenderer;

    [Header("Fill Settings")]
    public float fillDuration    = 3.0f;

    [Range(0f, 1f)]
    public float bpclTargetFill  = 0.72f;  // BPCL fills to 72% — cleaner, better flow
    [Range(0f, 1f)]
    public float localTargetFill = 0.68f;  // Local oil fills to 68% — poorer flow

    // Colors match OilParticleConfigurator values exactly
    private Color _bpclFillColor  = new Color(0.784f, 0.659f, 0.294f, 0.85f); // #C8A84B
    private Color _localFillColor = new Color(0.280f, 0.165f, 0.039f, 0.88f); // #47290A

    private Material  _fillMat;
    private float     _currentFill = 0f;
    private Coroutine _fillCoroutine;

    void Awake()
    {
        if (cavityRenderer == null)
            cavityRenderer = GetComponent<Renderer>();

        if (cavityRenderer == null)
        {
            Debug.LogError("[OilFillController] No Renderer found. Assign cavityRenderer.");
            return;
        }

        // Runtime instance — does not modify the shared material asset
        _fillMat = cavityRenderer.material;
        _fillMat.SetFloat("_FillLevel", 0f);
    }

    // -------------------------------------------------------
    // Public API — called by OilPourManager
    // -------------------------------------------------------

    /// <summary>Begin fill animation. oilType: 0 = BPCL, 1 = Local.</summary>
    public void BeginFilling(int oilType)
    {
        if (_fillMat == null) return;

        Color fillColor = (oilType == 0) ? _bpclFillColor : _localFillColor;
        _fillMat.SetColor("_OilColor", fillColor);

        float target = (oilType == 0) ? bpclTargetFill : localTargetFill;

        if (_fillCoroutine != null) StopCoroutine(_fillCoroutine);
        _fillCoroutine = StartCoroutine(AnimateFill(_currentFill, target, fillDuration));
    }

    /// <summary>SendMessage-compatible overload (called with string "0" or "1").</summary>
    public void BeginFilling(string oilTypeStr)
    {
        if (int.TryParse(oilTypeStr, out int t)) BeginFilling(t);
    }

    /// <summary>Instant reset to empty. Call on session reset.</summary>
    public void ResetFill()
    {
        if (_fillCoroutine != null) { StopCoroutine(_fillCoroutine); _fillCoroutine = null; }
        _currentFill = 0f;
        if (_fillMat != null) _fillMat.SetFloat("_FillLevel", 0f);
    }

    // -------------------------------------------------------
    // Private
    // -------------------------------------------------------

    private IEnumerator AnimateFill(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / duration);

            // Smoothstep — liquid feel, not linear
            float smooth = t * t * (3f - 2f * t);
            _currentFill = Mathf.Lerp(from, to, smooth);
            _fillMat.SetFloat("_FillLevel", _currentFill);
            yield return null;
        }
        _currentFill = to;
        _fillMat.SetFloat("_FillLevel", _currentFill);
        _fillCoroutine = null;
    }
}