using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// VR Oil Demo - Complete Implementation
/// 
/// Features:
/// - Bottles grabbed by VR controllers
/// - Auto-pour triggers when grabbed near the engine
/// - Forces bottle drop & assumes animation control (tilts and aligns with hole)
/// - Exact AR effects logic restored (Glow, Smoke, Piston Particles)
/// - Engine rises 1 meter upon completion
/// - Programmatic UI (Main Canvas in World Space + VR Benefits panel)
/// </summary>
public class VR_OilInteraction : MonoBehaviour
{
    private class PourScore
    {
        public readonly string Label;
        public bool Completed;
        public float DurationSeconds;
        public int StabilityScore;
        public int ControlScore;
        public int TimeScore;
        public int FinalScore;
        public int TiltDrops;

        public PourScore(string label) { Label = label; }
    }

    public enum OilType { None, BPCL_Premium, BPCL_Supreme, Local }
    public enum InteractionState
    {
        Idle,
        Holding,
        Pouring,
        PourComplete,
        PourFailed
    }

    #region === SERIALIZED FIELDS ===

    [Header("=== SCENE REFERENCES ===")]
    public GameObject engine;
    public GameObject bpclPremiumBottle;
    public GameObject bpclSupremeBottle;
    public GameObject localBottle;
    public Transform oilTankHole;
    public Camera vrCamera;
    
    [Header("=== VR INTERACTION ===")]
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable bpclPremiumGrabInteractable;
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable bpclSupremeGrabInteractable;
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable localGrabInteractable;
    [Tooltip("Max distance between bottle and oil tank to start pouring when grabbed")]
    public float maxPourDistance = 0.8f;
    
    [Header("=== POUR ANIMATION ===")]
    public float bottleApproachDuration = 0.6f;
    public float bottleTiltDuration = 0.8f;
    public float pourHoldDuration = 3.0f; 
    public float pourTiltAngle = 120f;
    public float pourTargetHeightOffset = 0.28f;
    public float minNeckHeightAboveOilHole = 0.12f;
    public float bottleCapHeight = 0.15f; 

    [Header("=== POST-POUR ANIMATION ===")]
    public float postPourRiseHeight = 1.0f;
    public float postPourRiseDuration = 3.0f;

    [Header("=== VISUAL FEEDBACK & VFX ===")]
    public OilPourVFXController oilVFXController;
    public OilPourManager oilPourManager;
    public ParticleSystem pouringParticles;

    [Header("=== ENGINE GLOW EFFECTS ===")]
    [Tooltip("The material that will glow golden after BPCL oil, and turn black after Normal oil.")]
    public Material engineGlowMaterial;
    [ColorUsage(true, true)] public Color bpclGlowColor = new Color(1f, 0.8f, 0f, 1f) * 2f;
    [ColorUsage(true, true)] public Color normalOilDarkColor = Color.black;
    public float engineGlowTransitionDuration = 2.0f;

    [Header("=== ENGINE BODY TRANSPARENCY ===")]
    public List<Material> engineTransparentMaterials = new List<Material>();
    [Range(0f, 1f)] public float engineBodyTransparentAlpha = 0.2f;
    public float engineBodyFadeDuration = 2f;

    [Header("=== ENGINE START UI ===")]
    public AudioClip startEngineSound;
    private GameObject startEnginePanel;
    private UnityEngine.UI.Button startEngineButton;

    [Header("=== AUDIO ===")]
    [Header("Voice Overs")]
    public AudioClip welcomeMessageAudio;
    public AudioClip bpclPremiumBenefitsAudio;
    public AudioClip bpclSupremeBenefitsAudio;
    public AudioClip localDemeritsAudio;

    [Header("SFX & Engine")]
    public AudioClip pouringSound;
    public AudioClip successSound;
    public AudioClip warningSound;
    public AudioClip defaultEngineLoop;
    public AudioClip normalOilEngineLoop;
    public AudioClip bpclOilEngineLoop;
    [Range(0f, 1f)] public float engineLoopVolume = 0.75f;
    public float audioTransitionDuration = 3.5f;

    [Header("=== VR UI ===")]
    public TMP_FontAsset customUIFont; // Assign your Jura-Regular SDF font asset here in the inspector
    public Sprite roundedPanelSprite; // Recommended: default "Background" or 9-sliced round sprite
    public Material glassBlurMaterial; // Recommended: UI Acrylic / Blur material
    public float vrPanelScale = 0.005f;
    public float panelDistanceFromEngine = 0.5f;
    public float panelHeightOffset = 0.4f;
    public Vector3 panelFacingRotationOffset = new Vector3(0f, 0f, 0f);
    public Vector3 mainCanvasOffset = new Vector3(0f, 0.8f, 0f);
    public Sprite bpclLogoSprite;
    public bool enableGuidedOnboarding = true;
    public bool enablePourScoring = true;

    [Header("=== UI FOLLOW SETTINGS ===")]
    public bool enableUIFollow = true;
    public float uiFollowDistance = 1.5f;
    public float uiFollowSpeed = 5f;
    public float uiFollowHeightOffset = -0.1f;

    #endregion

    #region === RUNTIME STATE ===

    private InteractionState currentState = InteractionState.Idle;
    private OilType activeBottleType = OilType.None;
    private GameObject activeBottle;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable activeGrabInteractable;

    private bool isHoldingBottle = false;
    private GameObject heldBottle;
    private OilType heldOilType;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable heldGrabInteractable;

    private float pourElapsedTime = 0f;
    private Vector3 selectedBottleOriginalPos;
    private Quaternion selectedBottleOriginalRot;
    private Quaternion selectedBottlePourBaseRot;
    
    private AudioSource sfxAudioSource;
    private AudioSource voiceAudioSource;
    private AudioSource engineAudioSource1;
    private AudioSource engineAudioSource2;
    private AudioSource activeEngineSource;
    private Coroutine audioCrossfadeCoroutine;
    
    // Smoke / Particle References
    private List<ParticleSystem> engineSmokeParticles = new List<ParticleSystem>();
    private List<ParticleSystem> engineOilBubbleParticles = new List<ParticleSystem>();
    private readonly Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> engineSmokeDefaultStartColors = new Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();
    
    // UI Runtime References
    private Canvas uiCanvas;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI instructionText;
    private GameObject resultPanel;
    private TextMeshProUGUI resultText;

    private GameObject bottomActivityPanel;
    private TextMeshProUGUI bottomActivityTitleText;
    private TextMeshProUGUI bottomActivityDetailText;
    private TextMeshProUGUI bottomActivityPhaseIconText;
    private Image bottomActivityIndicator;
    private RectTransform bottomActivityIndicatorRect;

    private GameObject vrBenefitsPanel;
    
    // State Tracking
    private bool hasSelectedAnyBottle = false;
    private bool hasCompletedBPCLPremium = false;
    private bool hasCompletedBPCLSupreme = false;
    private bool hasCompletedLocal = false;
    private readonly PourScore bpclPremiumScore = new PourScore("MAK Supreme 0W-20");
    private readonly PourScore bpclSupremeScore = new PourScore("MAK Supreme Synthesis 5W-30");
    private readonly PourScore localScore = new PourScore("Local Oil");
    private Coroutine sequentialInstructionCoroutine;
    private Coroutine hideBenefitsCoroutine;

    private Vector3 engineInitialPosition;
    private Quaternion engineInitialRotation;

    private Vector3 bpclPremiumDefaultPos = new Vector3(-5.69099998f, 1.06299996f, -4.75400019f);
    private Vector3 bpclSupremeDefaultPos = new Vector3(-5.98000002f, 1.06299996f, -4.54099989f); // Placed slightly next to it
    private Vector3 localDefaultPos = new Vector3(-5.0079999f, 1.07099998f, -4.75f);
    private Quaternion bpclPremiumDefaultRot;
    private Quaternion bpclSupremeDefaultRot;
    private Quaternion localDefaultRot;

    #endregion

    #region === LIFECYCLE ===

    private void Awake()
    {
        EnsureConvexColliders(bpclPremiumBottle);
        EnsureConvexColliders(bpclSupremeBottle);
        EnsureConvexColliders(localBottle);
        
        InitializeAudio();
        SetupGrabListeners();

        if (vrCamera == null)
            vrCamera = Camera.main;
    }

    private void EnsureConvexColliders(GameObject bottle)
    {
        if (bottle == null) return;
        MeshCollider[] colliders = bottle.GetComponentsInChildren<MeshCollider>(true);
        foreach (MeshCollider col in colliders)
        {
            if (!col.convex)
            {
                col.convex = true;
            }
        }
    }

    private void Start()
    {
        if (engine != null)
        {
            engineInitialPosition = engine.transform.position;
            engineInitialRotation = engine.transform.rotation;
        }
        
        if (bpclPremiumBottle != null) bpclPremiumDefaultRot = bpclPremiumBottle.transform.rotation;
        if (bpclSupremeBottle != null) bpclSupremeDefaultRot = bpclSupremeBottle.transform.rotation;
        if (localBottle != null) localDefaultRot = localBottle.transform.rotation;

        CreateUI();

        PauseEngineAnimation();
        
        UpdateStatus("●  Ready");
        sequentialInstructionCoroutine = StartCoroutine(ShowSequentialInstructions(
            "Select an oil",
            "Grab a bottle to begin"
        ));
        
        if (welcomeMessageAudio != null)
        {
            voiceAudioSource.clip = welcomeMessageAudio;
            voiceAudioSource.Play();
        }
        
        FindEngineSmokeParticles();
        FindOilBubbleParticles();
    }

    private void Update()
    {
        if (isHoldingBottle && (currentState == InteractionState.Idle || currentState == InteractionState.PourFailed))
        {
            if (heldBottle != null)
            {
                float distance = Vector3.Distance(heldBottle.transform.position, GetOilTankPosition());
                if (distance <= maxPourDistance)
                {
                    activeBottleType = heldOilType;
                    activeBottle = heldBottle;
                    activeGrabInteractable = heldGrabInteractable;
                    
                    isHoldingBottle = false;
                    StartPouring();
                }
            }
        }

        if (currentState == InteractionState.Pouring)
        {
            UpdatePouringState();
        }

        UpdateBottomActivityVisualPulse();
    }

    private void LateUpdate()
    {
        if (enableUIFollow && vrCamera != null)
        {
            // Calculate target position in front of camera
            Vector3 forwardLevel = vrCamera.transform.forward;
            forwardLevel.y = 0f; // Keep it level relative to world up
            if (forwardLevel.sqrMagnitude < 0.001f) forwardLevel = vrCamera.transform.up;
            forwardLevel.Normalize();

            Vector3 baseTargetPos = vrCamera.transform.position + 
                              forwardLevel * uiFollowDistance + 
                              Vector3.up * uiFollowHeightOffset;

            if (uiCanvas != null)
            {
                uiCanvas.transform.position = Vector3.Lerp(uiCanvas.transform.position, baseTargetPos, Time.deltaTime * uiFollowSpeed);

                Vector3 lookDir = uiCanvas.transform.position - vrCamera.transform.position;
                lookDir.y = 0;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized);
                    uiCanvas.transform.rotation = Quaternion.Slerp(uiCanvas.transform.rotation, targetRot, Time.deltaTime * uiFollowSpeed * 1.5f);
                }
            }

            if (vrBenefitsPanel != null)
            {
                Vector3 rightDir = -vrCamera.transform.right;
                rightDir.y = 0;
                rightDir.Normalize();
                
                Vector3 panelTargetPos = baseTargetPos + rightDir * panelDistanceFromEngine;

                vrBenefitsPanel.transform.position = Vector3.Lerp(vrBenefitsPanel.transform.position, panelTargetPos, Time.deltaTime * uiFollowSpeed);

                Quaternion targetRot = Quaternion.LookRotation(vrBenefitsPanel.transform.position - vrCamera.transform.position);
                vrBenefitsPanel.transform.rotation = Quaternion.Slerp(vrBenefitsPanel.transform.rotation, targetRot, Time.deltaTime * uiFollowSpeed);
            }
        }
    }

    #endregion

    #region === UI CREATION & LOGIC ===

    private void CreateUI()
    {
        // Create Canvas in World Space
        GameObject canvasObj = new GameObject("VR_Oil_Canvas");
        uiCanvas = canvasObj.AddComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.WorldSpace;
        uiCanvas.sortingOrder = 100;
        
        // Ensure Canvas can receive raycasts from VR controllers
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        canvasObj.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

        // Size it like the AR Canvas but make it small for VR
        RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1080, 1920);
        canvasObj.transform.localScale = Vector3.one * vrPanelScale;

        // Position Canvas above engine
        if (engine != null)
        {
            canvasObj.transform.position = engine.transform.position + mainCanvasOffset;
            if (vrCamera != null)
            {
                Vector3 lookDir = canvasObj.transform.position - vrCamera.transform.position;
                lookDir.y = 0;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    canvasObj.transform.rotation = Quaternion.LookRotation(lookDir.normalized);
                }
            }
        }
        
        // === POUR PROGRESS REMOVED ===

        bottomActivityPanel = CreateGlassPanel(canvasObj.transform, "BottomActivityPanel",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 80f), new Vector2(480f, 90f),
            new Color(0.02f, 0.03f, 0.05f, 0.85f));
        AddPanelBorder(bottomActivityPanel, new Color(0.2f, 0.5f, 0.4f, 0.3f), 1f);

        GameObject indicatorObj = new GameObject("ActivityIndicator");
        indicatorObj.transform.SetParent(bottomActivityPanel.transform, false);
        bottomActivityIndicatorRect = indicatorObj.AddComponent<RectTransform>();
        bottomActivityIndicatorRect.anchorMin = new Vector2(0f, 0.5f);
        bottomActivityIndicatorRect.anchorMax = new Vector2(0f, 0.5f);
        bottomActivityIndicatorRect.pivot = new Vector2(0.5f, 0.5f);
        bottomActivityIndicatorRect.anchoredPosition = new Vector2(40f, 0f);
        bottomActivityIndicatorRect.sizeDelta = new Vector2(18f, 18f);
        bottomActivityIndicator = indicatorObj.AddComponent<Image>();
        bottomActivityIndicator.color = new Color(0.37f, 0.97f, 0.68f, 0.95f);
        if (roundedPanelSprite != null) bottomActivityIndicator.sprite = roundedPanelSprite;

        bottomActivityPhaseIconText = CreateMinimalText(bottomActivityPanel.transform, "ActivityPhaseIcon",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(85f, 0f), new Vector2(40f, 40f),
            "<color=#86EFAC>◎</color>", 30, TextAlignmentOptions.Center,
            new Color(0.85f, 0.95f, 0.92f, 1f));
        bottomActivityPhaseIconText.fontStyle = FontStyles.Bold;
        bottomActivityPhaseIconText.enableWordWrapping = false;

        bottomActivityTitleText = CreateMinimalText(bottomActivityPanel.transform, "ActivityTitle",
            new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(125f, 18f), new Vector2(-145f, 30f),
            "POURING", 22, TextAlignmentOptions.Left,
            new Color(0.9f, 0.98f, 0.95f, 1f));
        bottomActivityTitleText.fontStyle = FontStyles.Bold;
        bottomActivityTitleText.characterSpacing = 2f;

        bottomActivityDetailText = CreateMinimalText(bottomActivityPanel.transform, "ActivityDetail",
            new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(125f, -15f), new Vector2(-145f, 28f),
            "Aligning bottle to oil inlet", 18, TextAlignmentOptions.Left,
            new Color(0.62f, 0.82f, 0.75f, 0.8f));

        bottomActivityPanel.SetActive(false);
        
        // === RESULT PANEL ===
        resultPanel = CreateGlassPanel(canvasObj.transform, "ResultPanel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 120), new Vector2(380, 100), 
            new Color(0.02f, 0.03f, 0.04f, 0.85f));
        
        AddPanelBorder(resultPanel, new Color(0.2f, 0.3f, 0.4f, 0.3f), 1f);
        
        resultText = CreateMinimalText(resultPanel.transform, "ResultText",
            new Vector2(0, 0), new Vector2(1, 1),
            Vector2.zero, new Vector2(-30, -20),
            "", 22, TextAlignmentOptions.Center,
            Color.white);
        resultText.fontStyle = FontStyles.Bold;
        resultPanel.SetActive(false);

        // === START ENGINE PANEL (SLEEK REDESIGN) ===
        startEnginePanel = CreateGlassPanel(canvasObj.transform, "StartEnginePanel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -60), new Vector2(400, 150), 
            new Color(0.01f, 0.02f, 0.03f, 0.75f)); // More transparent glass
        AddPanelBorder(startEnginePanel, new Color(0.8f, 0.9f, 1f, 0.2f), 1f); // Soft subtle border

        TextMeshProUGUI startInstructionTxt = CreateMinimalText(startEnginePanel.transform, "StartInstruction",
            new Vector2(0, 0.5f), new Vector2(1, 1), 
            new Vector2(0, -15), new Vector2(0, -20),
            "<size=110%>Awaiting Ignition...</size>\n<color=#A0B0C0><size=80%>Tap to start the engine</size></color>", 
            20, TextAlignmentOptions.Center,
            new Color(0.9f, 0.95f, 1f, 1f));
        
        GameObject btnObj = new GameObject("StartEngineButton");
        btnObj.transform.SetParent(startEnginePanel.transform, false);
        RectTransform btnRect = btnObj.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0f);
        btnRect.anchorMax = new Vector2(0.5f, 0f);
        btnRect.pivot = new Vector2(0.5f, 0f);
        btnRect.anchoredPosition = new Vector2(0, 20);
        btnRect.sizeDelta = new Vector2(300, 55);
        
        Image btnImg = btnObj.AddComponent<Image>();
        btnImg.color = new Color(0.95f, 0.95f, 0.95f, 0.95f); // White/bright button for sleek iOS feel
        btnImg.raycastTarget = true;
        if (roundedPanelSprite != null)
        {
            btnImg.sprite = roundedPanelSprite;
            btnImg.type = Image.Type.Sliced;
        }
        
        startEngineButton = btnObj.AddComponent<UnityEngine.UI.Button>();
        startEngineButton.onClick.AddListener(OnStartEngineClicked);
        
        // Removed heavy border for the inner button, replaced with a subtle drop shadow/glow via text color
        TextMeshProUGUI startTxt = CreateMinimalText(btnObj.transform, "StartText",
            new Vector2(0, 0), new Vector2(1, 1),
            Vector2.zero, Vector2.zero,
            "START ENGINE", 22, TextAlignmentOptions.Center,
            new Color(0.05f, 0.05f, 0.05f, 1f)); // Dark text on light background
        startTxt.fontStyle = FontStyles.Bold;
        startTxt.characterSpacing = 3f;
        startEnginePanel.SetActive(false);

        Debug.Log("[VR_Oil] Programmatic UI created");
    }
    
    private GameObject CreateMinimalPanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color bgColor)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        
        Image img = obj.AddComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = false;
        
        if (roundedPanelSprite != null)
        {
            img.sprite = roundedPanelSprite;
            img.type = Image.Type.Sliced;
        }

        return obj;
    }
    
    private GameObject CreateGlassPanel(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Color bgColor)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        
        Image img = obj.AddComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = true; // explicitly enabling raycast on panels so buttons inside them work cleanly

        if (roundedPanelSprite != null)
        {
            img.sprite = roundedPanelSprite;
            img.type = Image.Type.Sliced;
        }

        if (glassBlurMaterial != null)
        {
            img.material = glassBlurMaterial;
        }

        return obj;
    }
    
    private void AddPanelBorder(GameObject panel, Color borderColor, float thickness)
    {
        GameObject border = new GameObject("Border");
        border.transform.SetParent(panel.transform, false);
        
        RectTransform rect = border.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-thickness, -thickness);
        rect.offsetMax = new Vector2(thickness, thickness);
        
        Outline outline = panel.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = new Vector2(thickness, thickness);
    }
    
    private TextMeshProUGUI CreateMinimalText(Transform parent, string name, 
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 sizeDelta,
        string text, int fontSize, TextAlignmentOptions alignment, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        if (customUIFont != null) tmp.font = customUIFont;
        
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        
        return tmp;
    }

    private IEnumerator PulseTextEffect(TextMeshProUGUI tmp, float scaleMultiplier, float duration)
    {
        if (tmp == null) yield break;
        
        Vector3 origScale = tmp.rectTransform.localScale;
        Vector3 targetScale = origScale * scaleMultiplier;
        
        float halfDur = duration / 2f;
        float elapsed = 0f;
        
        while (elapsed < halfDur)
        {
            elapsed += Time.deltaTime;
            tmp.rectTransform.localScale = Vector3.Lerp(origScale, targetScale, elapsed / halfDur);
            yield return null;
        }
        
        elapsed = 0f;
        while (elapsed < halfDur)
        {
            elapsed += Time.deltaTime;
            tmp.rectTransform.localScale = Vector3.Lerp(targetScale, origScale, elapsed / halfDur);
            yield return null;
        }
        
        tmp.rectTransform.localScale = origScale;
    }

    private IEnumerator ShowSequentialInstructions(params string[] instructions)
    {
        if (resultPanel == null || resultText == null) yield break;

        yield return null; 

        resultPanel.SetActive(false);

        foreach (var instruction in instructions)
        {
            resultText.text = instruction;
            resultPanel.SetActive(true);
            yield return new WaitForSeconds(2.5f);
            resultPanel.SetActive(false);
            yield return new WaitForSeconds(0.5f); 
        }
    }

    private IEnumerator ShowSingleInstruction(string instruction)
    {
        if (resultPanel == null || resultText == null) yield break;
        yield return null;

        resultText.text = instruction;
        resultPanel.SetActive(true);
        yield return new WaitForSeconds(2.5f);
        resultPanel.SetActive(false);
    }

    private void SetBottomActivityState(string title, string detail, string phaseIcon)
    {
        if (bottomActivityPanel == null) return;

        if (bottomActivityTitleText != null) bottomActivityTitleText.text = title;
        if (bottomActivityDetailText != null) bottomActivityDetailText.text = detail;
        if (bottomActivityPhaseIconText != null) bottomActivityPhaseIconText.text = phaseIcon;

        SetBottomActivityVisible(true);
    }

    private void SetBottomActivityVisible(bool visible)
    {
        if (bottomActivityPanel != null) bottomActivityPanel.SetActive(visible);
    }

    private void UpdateBottomActivityVisualPulse()
    {
        if (bottomActivityPanel == null || !bottomActivityPanel.activeSelf || bottomActivityIndicator == null)
            return;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.2f);
        Color c = bottomActivityIndicator.color;
        c.a = Mathf.Lerp(0.45f, 0.98f, pulse);
        bottomActivityIndicator.color = c;

        if (bottomActivityIndicatorRect != null)
        {
            float scale = Mathf.Lerp(0.9f, 1.12f, pulse);
            bottomActivityIndicatorRect.localScale = new Vector3(scale, scale, 1f);
        }
    }

    private void UpdateStatus(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }
    
    private void UpdateInstructions(string text)
    {
        if (instructionText != null)
        {
            if (instructionText.text != text && !string.IsNullOrEmpty(text))
            {
                instructionText.text = text;
                StartCoroutine(PulseTextEffect(instructionText, 1.15f, 0.4f));
            }
            else
            {
                instructionText.text = text;
            }
        }
    }

    private void ShowResult(string text, Color bgColor)
    {
        if (resultPanel != null && resultText != null)
        {
            resultText.text = text;
            resultPanel.GetComponent<Image>().color = bgColor;
            resultPanel.SetActive(true);
        }
    }
    
    private void HideResult()
    {
        if (resultPanel != null)
        {
            resultPanel.SetActive(false);
        }
    }

    private void ShowUIMessage(string message)
    {
        if (!resultText || !resultPanel) return;
        resultText.text = message;
        resultPanel.SetActive(true);
        CancelInvoke(nameof(HideUIMessage));
        Invoke(nameof(HideUIMessage), 4f);
    }
    private void HideUIMessage() { if (resultPanel) resultPanel.SetActive(false); }

    private string GetGuidedInstruction(InteractionState state)
    {
        if (!enableGuidedOnboarding)
        {
            return state switch
            {
                InteractionState.Holding => "Tilt bottle to pour",
                InteractionState.Pouring => "Keep pouring...",
                InteractionState.PourComplete => "Grab another bottle to compare",
                _ => "Grab a bottle to select"
            };
        }

        if (!hasSelectedAnyBottle)
            return "Step 1/3 Grab a bottle to begin";

        if (state == InteractionState.Holding)
            return "Step 2/3 Move bottle to engine to pour";

        if (state == InteractionState.Pouring)
            return "Step 2/3 Pouring...";

        if (state == InteractionState.PourComplete)
            return "Step 3/3 See results and try another";

        if ((hasCompletedBPCLPremium || hasCompletedBPCLSupreme) && hasCompletedLocal)
            return "Comparison ready. Grab any bottle to retry";

        if (hasCompletedBPCLPremium || hasCompletedBPCLSupreme || hasCompletedLocal)
            return "Grab the other bottle to compare";

        return "Grab a bottle to begin";
    }

    #endregion

    #region === BENEFITS PANEL ===

    private void ShowBPCLPremiumBenefits()
    {
        string benefits = @"<b>MAK Supreme</b>
" +
@"<size=80%><color=#4ADE80>0W-20</color></size>

" +
@"<color=#86EFAC>✓</color> Fuel Economy
" +
@"<color=#86EFAC>✓</color> Smooth Start
" +
@"<color=#86EFAC>✓</color> Engine Protection";

        CreateVRBenefitsPanel(benefits, new Color(0.08f, 0.15f, 0.12f, 0.95f), true);

        if (bpclPremiumBenefitsAudio != null)
        {
            voiceAudioSource.clip = bpclPremiumBenefitsAudio;
            voiceAudioSource.Play();
        }
    }

    private void ShowBPCLSupremeBenefits()
    {
        string benefits = @"<b>MAK Supreme</b>
" +
@"<size=80%><color=#4ADE80>Synthetic 5W-30</color></size>

" +
@"<color=#86EFAC>✓</color> More Mileage
" +
@"<color=#86EFAC>✓</color> Long Life
" +
@"<color=#86EFAC>✓</color> All Weather
" +
@"<color=#86EFAC>✓</color> Peak Performance";

        CreateVRBenefitsPanel(benefits, new Color(0.08f, 0.15f, 0.12f, 0.95f), true);

        if (bpclSupremeBenefitsAudio != null)
        {
            voiceAudioSource.clip = bpclSupremeBenefitsAudio;
            voiceAudioSource.Play();
        }
    }
    
    private void ShowLocalOilDemerits()
    {
        string demerits = @"<b>Mineral Lube</b>
" +
@"<size=80%><color=#F87171>Standard</color></size>

" +
@"<color=#FCA5A5>✗</color> Less Mileage
" +
@"<color=#FCA5A5>✗</color> Short Life
" +
@"<color=#FCA5A5>✗</color> Limited Use
" +
@"<color=#FCA5A5>✗</color> Slow Start";

        CreateVRBenefitsPanel(demerits, new Color(0.18f, 0.08f, 0.08f, 0.95f), false);

        if (localDemeritsAudio != null)
        {
            voiceAudioSource.clip = localDemeritsAudio;
            voiceAudioSource.Play();
        }
    }
    
    private void HideVRBenefitsPanel()
    {
        if (hideBenefitsCoroutine != null)
        {
            StopCoroutine(hideBenefitsCoroutine);
            hideBenefitsCoroutine = null;
        }

        if (vrBenefitsPanel != null)
        {
            Destroy(vrBenefitsPanel);
            vrBenefitsPanel = null;
        }
    }

    private void CreateVRBenefitsPanel(string text, Color bgColor, bool isPositive)
    {
        HideVRBenefitsPanel();
        
        if (engine == null) return;
        
        vrBenefitsPanel = new GameObject("VR_Benefits_Panel");
        
        Vector3 enginePos = engine.transform.position;
        Vector3 rightDir = vrCamera != null ? -vrCamera.transform.right : -Vector3.right;
        rightDir.y = 0;
        rightDir.Normalize();
        
        Vector3 panelWorldPos = enginePos 
            + rightDir * panelDistanceFromEngine 
            + Vector3.up * panelHeightOffset;
        
        vrBenefitsPanel.transform.position = panelWorldPos;
        
        if (vrCamera != null)
        {
            vrBenefitsPanel.transform.rotation = Quaternion.LookRotation(vrBenefitsPanel.transform.position - vrCamera.transform.position);
        }
        else
        {
            vrBenefitsPanel.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        }
        
        Canvas canvas = vrBenefitsPanel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        RectTransform canvasRect = vrBenefitsPanel.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(240, 180); // Made slightly larger to feel less cramped
        
        vrBenefitsPanel.transform.localScale = Vector3.one * 0.004f; // Hardcoded scale to ensure it updates despite Unity Inspector
        
        CreatePanelBackground(bgColor, isPositive);
        CreatePanelText(text);
        
        hideBenefitsCoroutine = StartCoroutine(HideBenefitsPanelAfterDelay(5f));
        
        Debug.Log($"[VR_Oil] VR Benefits panel created - Position: {panelWorldPos}, Scale: {vrPanelScale}");
    }
    
    private IEnumerator HideBenefitsPanelAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        HideVRBenefitsPanel();
    }
    
    private void CreatePanelBackground(Color bgColor, bool isPositive)
    {
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(vrBenefitsPanel.transform, false);
        
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = bgColor;
        if (roundedPanelSprite != null)
        {
            bgImage.sprite = roundedPanelSprite;
            bgImage.type = Image.Type.Sliced;
        }

        if (glassBlurMaterial != null)
        {
            bgImage.material = glassBlurMaterial;
        }
        
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(bgObj.transform, false);
        
        RectTransform borderRect = borderObj.AddComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = new Vector2(-1.5f, -1.5f);
        borderRect.offsetMax = new Vector2(1.5f, 1.5f);
        borderRect.SetAsFirstSibling();
        
        Image borderImage = borderObj.AddComponent<Image>();
        borderImage.color = isPositive 
            ? new Color(0.2f, 0.6f, 0.4f, 0.2f) // More subtle green border
            : new Color(0.6f, 0.2f, 0.2f, 0.2f); // More subtle red border

        if (roundedPanelSprite != null)
        {
            borderImage.sprite = roundedPanelSprite;
            borderImage.type = Image.Type.Sliced;
        }
        
        GameObject accentLine = new GameObject("AccentLine");
        accentLine.transform.SetParent(bgObj.transform, false);
        
        RectTransform accentRect = accentLine.AddComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0, 1);
        accentRect.anchorMax = new Vector2(1, 1);
        accentRect.pivot = new Vector2(0.5f, 1);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(0, 3);
        
        Image accentImage = accentLine.AddComponent<Image>();
        accentImage.color = isPositive
            ? new Color(0.29f, 0.87f, 0.5f, 1f)
            : new Color(0.97f, 0.45f, 0.45f, 1f);
    }
    
    private void CreatePanelText(string text)
    {
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(vrBenefitsPanel.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(15, 15); // Added generous padding
        textRect.offsetMax = new Vector2(-15, -15);
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        if (customUIFont != null) tmp.font = customUIFont;
        
        tmp.text = text;
        tmp.fontSize = 14; // Slightly increased for readability
        tmp.color = new Color(0.95f, 0.97f, 1f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.lineSpacing = 5f; // Added gentle line-height for better scanning
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }

    #endregion

    #region === AUDIO SETUP ===

    private void InitializeAudio()
    {
        sfxAudioSource = gameObject.AddComponent<AudioSource>();
        sfxAudioSource.playOnAwake = false;

        voiceAudioSource = gameObject.AddComponent<AudioSource>();
        voiceAudioSource.playOnAwake = false;
        voiceAudioSource.spatialBlend = 0f; // 2D sound for clear voice instructions

        engineAudioSource1 = gameObject.AddComponent<AudioSource>();
        engineAudioSource1.loop = true;
        engineAudioSource1.volume = engineLoopVolume;
        engineAudioSource1.spatialBlend = 1f;

        engineAudioSource2 = gameObject.AddComponent<AudioSource>();
        engineAudioSource2.loop = true;
        engineAudioSource2.volume = 0f;
        engineAudioSource2.spatialBlend = 1f;

        activeEngineSource = engineAudioSource1;
    }

    #endregion

    #region === VR INTERACTION (GRAB) ===

    private void SetupGrabListeners()
    {
        if (bpclPremiumGrabInteractable)
        {
            bpclPremiumGrabInteractable.selectEntered.AddListener((args) => OnBottleGrabbed(OilType.BPCL_Premium, bpclPremiumBottle, bpclPremiumGrabInteractable));
            bpclPremiumGrabInteractable.selectExited.AddListener((args) => OnBottleReleased());
        }
        
        if (bpclSupremeGrabInteractable)
        {
            bpclSupremeGrabInteractable.selectEntered.AddListener((args) => OnBottleGrabbed(OilType.BPCL_Supreme, bpclSupremeBottle, bpclSupremeGrabInteractable));
            bpclSupremeGrabInteractable.selectExited.AddListener((args) => OnBottleReleased());
        }

        if (localGrabInteractable)
        {
            localGrabInteractable.selectEntered.AddListener((args) => OnBottleGrabbed(OilType.Local, localBottle, localGrabInteractable));
            localGrabInteractable.selectExited.AddListener((args) => OnBottleReleased());
        }
    }

    private void OnBottleGrabbed(OilType oilType, GameObject bottle, UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable)
    {
        if (currentState == InteractionState.PourComplete && engine != null)
        {
            engine.transform.position = engineInitialPosition;
            engine.transform.rotation = engineInitialRotation;
            StopEngineSmokeParticles();
            StopOilBubbleParticles();
            HideVRBenefitsPanel();
            
            PauseEngineAnimation();
            engineAudioSource1.Stop();
            engineAudioSource2.Stop();
            startEnginePanel?.SetActive(false);

            currentState = InteractionState.Idle;
            UpdateInstructions(GetGuidedInstruction(currentState));
        }

        if (currentState != InteractionState.Idle && currentState != InteractionState.PourFailed) return;

        hasSelectedAnyBottle = true;
        isHoldingBottle = true;
        heldOilType = oilType;
        heldBottle = bottle;
        heldGrabInteractable = grabInteractable;

        string bottleName = oilType == OilType.BPCL_Premium ? "MAK Supreme" : (oilType == OilType.BPCL_Supreme ? "MAK Supreme" : "Local Oil");
        UpdateStatus($"◆  {bottleName}");
        
        if (sequentialInstructionCoroutine != null) StopCoroutine(sequentialInstructionCoroutine);
        sequentialInstructionCoroutine = StartCoroutine(ShowSingleInstruction("Prepare to pour"));
        
        UpdateInstructions(GetGuidedInstruction(InteractionState.Holding));

        float distance = Vector3.Distance(bottle.transform.position, GetOilTankPosition());
        if (distance > maxPourDistance)
        {
            UpdateStatus("Bring closer to engine");
            if (warningSound && !sfxAudioSource.isPlaying) sfxAudioSource.PlayOneShot(warningSound);
        }
    }

    private void OnBottleReleased()
    {
        isHoldingBottle = false;
        heldBottle = null;
        heldGrabInteractable = null;
        
        if (currentState == InteractionState.Idle || currentState == InteractionState.PourFailed)
        {
            UpdateStatus("●  Ready");
            UpdateInstructions(GetGuidedInstruction(InteractionState.Idle));
        }
    }

    #endregion

    #region === STATE MACHINE & ANIMATION ===

    private void StartPouring()
    {
        if (activeGrabInteractable != null)
        {
            activeGrabInteractable.enabled = false; 
            
            Rigidbody rb = activeBottle.GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;
        }

        selectedBottleOriginalPos = activeBottle.transform.position;
        selectedBottleOriginalRot = activeBottle.transform.rotation;
        Vector3 toEngine = GetOilTankPosition() - selectedBottleOriginalPos;
        toEngine.y = 0;
        selectedBottlePourBaseRot = toEngine.sqrMagnitude > 0.001f ? Quaternion.LookRotation(toEngine.normalized) : activeBottle.transform.rotation;

        pourElapsedTime = 0f;
        TransitionToState(InteractionState.Pouring);
    }

    private void UpdatePouringState()
    {
        if (activeBottle == null)
        {
            FailPour("Bottle lost!");
            return;
        }

        pourElapsedTime += Time.deltaTime;
        AnimateBottlePouring(pourElapsedTime);

        float approach = bottleApproachDuration;
        float tilt = bottleTiltDuration;
        float totalDuration = approach + tilt + pourHoldDuration;

        if (pourElapsedTime > approach + tilt)
        {
            if (pouringParticles == null || !pouringParticles.isPlaying)
            {
                UpdatePourParticles(activeBottle);
            }
        }

        if (pourElapsedTime >= totalDuration)
        {
            CompletePour();
        }
    }

    private void PauseEngineAnimation()
    {
        if (engine == null) return;
        Animator[] animators = engine.GetComponentsInChildren<Animator>(true);
        foreach (var anim in animators)
        {
            if (anim != null) anim.speed = 0f;
        }
    }

    private void ResumeEngineAnimation()
    {
        if (engine == null) return;
        Animator[] animators = engine.GetComponentsInChildren<Animator>(true);
        foreach (var anim in animators)
        {
            if (anim != null) anim.speed = 1f;
        }
    }

    public void OnStartEngineClicked()
    {
        startEnginePanel.SetActive(false);
        StartCoroutine(StartEngineSequence());
    }

    private IEnumerator StartEngineSequence()
    {
        // 1. Play Start Engine sound on the engine source to allow crossfading
        if (startEngineSound != null)
        {
            activeEngineSource.loop = false;
            activeEngineSource.clip = startEngineSound;
            activeEngineSource.volume = engineLoopVolume;
            activeEngineSource.Play();
        }
        
        // Resume basic animation
        ResumeEngineAnimation();

        // 2. Wait exactly 2 seconds before merging to main engine loop
        yield return new WaitForSeconds(2.0f);

        AudioClip targetEngineClip = (activeBottleType == OilType.BPCL_Premium || activeBottleType == OilType.BPCL_Supreme) ? bpclOilEngineLoop : normalOilEngineLoop;
        // The crossfade method handles the smooth transition of the start sound fading out and loop fading in
        CrossfadeEngineLoopClip(targetEngineClip, audioTransitionDuration);

        // 3. Wait another 2 seconds for audio to stabilize before the visual event occurs
        yield return new WaitForSeconds(2.0f);

        if (activeBottleType == OilType.BPCL_Premium || activeBottleType == OilType.BPCL_Supreme)
        {
            ApplyBPCLEffect();
        }
        else
        {
            ApplyLocalOilEffect();
        }

        // Engine Rise Event
        StartCoroutine(RiseEngineForInspection(engine, postPourRiseHeight, postPourRiseDuration));

        // 4. Wait another 2 seconds before the benefits panel appears
        yield return new WaitForSeconds(2.0f);

        if (activeBottleType == OilType.BPCL_Premium)
        {
            ShowBPCLPremiumBenefits();
        }
        else if (activeBottleType == OilType.BPCL_Supreme)
        {
            ShowBPCLSupremeBenefits();
        }
        else
        {
            ShowLocalOilDemerits();
        }
    }

    private void CompletePour()
    {
        if (successSound) sfxAudioSource.PlayOneShot(successSound);

        string message = "Local oil applied.";
        if (activeBottleType == OilType.BPCL_Premium) message = "Excellent! MAK Supreme 0W-20 applied.";
        else if (activeBottleType == OilType.BPCL_Supreme) message = "Excellent! MAK Supreme Synthesis 5W-30 applied.";

        ShowUIMessage(message);

        if (activeBottleType == OilType.BPCL_Premium) hasCompletedBPCLPremium = true;
        else if (activeBottleType == OilType.BPCL_Supreme) hasCompletedBPCLSupreme = true;
        else hasCompletedLocal = true;

        TransitionToState(InteractionState.PourComplete);

        StartCoroutine(PostPourSequence());
    }

    private IEnumerator PostPourSequence()
    {
        Vector3 returnPos;
        Quaternion returnRot;

        if (activeBottleType == OilType.BPCL_Premium)
        {
            returnPos = bpclPremiumDefaultPos;
            returnRot = bpclPremiumDefaultRot;
        }
        else if (activeBottleType == OilType.BPCL_Supreme)
        {
            returnPos = bpclSupremeDefaultPos;
            returnRot = bpclSupremeDefaultRot;
        }
        else
        {
            returnPos = localDefaultPos;
            returnRot = localDefaultRot;
        }

        yield return StartCoroutine(ReturnBottleToOrigin(activeBottle, returnPos, returnRot, postPourRiseDuration));

        if (startEnginePanel != null) startEnginePanel.SetActive(true);
    }

    private void FailPour(string reason)
    {
        ShowUIMessage(reason);
        TransitionToState(InteractionState.PourFailed);
    }

    private void TransitionToState(InteractionState newState)
    {
        if (currentState == InteractionState.Pouring)
        {
            StopPourParticles();
            sfxAudioSource.Stop(); 
            SetBottomActivityVisible(false);
        }

        currentState = newState;

        switch (newState)
        {
            case InteractionState.Idle:
                UpdateStatus("●  Ready");
                UpdateInstructions(GetGuidedInstruction(newState));
                break;

            case InteractionState.Pouring:
                UpdateStatus("▼  Pouring");
                UpdateInstructions(GetGuidedInstruction(newState));
                SetBottomActivityState("POURING", "Aligning bottle to oil inlet", "<color=#86EFAC>◎</color>");
                
                if (pouringSound) sfxAudioSource.PlayOneShot(pouringSound);
                
                Transform nozzle = activeBottle.transform.Find("NozzlePoint");
                if (oilVFXController) oilVFXController.OnPourStart(activeBottleType == OilType.BPCL_Premium || activeBottleType == OilType.BPCL_Supreme ? 0 : 1, nozzle ? nozzle : activeBottle.transform);
                if (oilPourManager) oilPourManager.OnPourStart(activeBottleType == OilType.BPCL_Premium || activeBottleType == OilType.BPCL_Supreme ? 0 : 1, activeBottle);
                break;
                
            case InteractionState.PourComplete:
                UpdateStatus("✓  Complete");
                UpdateInstructions(GetGuidedInstruction(newState));
                if (oilVFXController) oilVFXController.OnPourComplete();
                if (oilPourManager) oilPourManager.OnPourComplete();
                break;

            case InteractionState.PourFailed:
                UpdateStatus("⚠  Failed");
                
                if (oilVFXController) oilVFXController.OnPourFailed();
                if (oilPourManager) oilPourManager.OnPourFailed();
                
                if (activeGrabInteractable != null) activeGrabInteractable.enabled = true;
                
                StartCoroutine(ResetFailedStateAfterDelay(2f));
                break;
        }
    }

    private void AnimateBottlePouring(float elapsedTime)
    {
        if (activeBottle == null) return;

        Vector3 oilPos = GetOilTankPosition();
        Vector3 startPos = selectedBottleOriginalPos;
        
        Vector3 targetPos = oilPos + Vector3.up * pourTargetHeightOffset;
        Vector3 toBottle = (startPos - oilPos);
        toBottle.y = 0;
        if (toBottle.sqrMagnitude > 0.001f)
            targetPos += toBottle.normalized * 0.1f; 

        float approach = bottleApproachDuration;
        float tilt = bottleTiltDuration;

        if (elapsedTime < approach)
        {
            float t = elapsedTime / approach;
            t = t * t * (3f - 2f * t); 

            Vector3 controlPoint = startPos + Vector3.up * 0.2f;
            float u = 1f - t;
            Vector3 pos = u * u * startPos + 2f * u * t * controlPoint + t * t * targetPos;
            
            activeBottle.transform.position = pos;
            activeBottle.transform.rotation = Quaternion.Slerp(activeBottle.transform.rotation, selectedBottlePourBaseRot, t);
        }
        else if (elapsedTime < approach + tilt)
        {
            float t = (elapsedTime - approach) / tilt;
            t = t * t * (3f - 2f * t);

            activeBottle.transform.position = targetPos;
            
            Quaternion tiltedRot = selectedBottlePourBaseRot * Quaternion.Euler(pourTiltAngle, 0, 0);
            activeBottle.transform.rotation = Quaternion.Slerp(selectedBottlePourBaseRot, tiltedRot, t);

            if (t > 0.2f) AlignBottleCapAboveOilHole(activeBottle, oilPos);
        }
        else
        {
            Quaternion finalRot = selectedBottlePourBaseRot * Quaternion.Euler(pourTiltAngle, 0, 0);
            activeBottle.transform.rotation = finalRot;
            AlignBottleCapAboveOilHole(activeBottle, oilPos);
        }
    }

    private bool AlignBottleCapAboveOilHole(GameObject bottle, Vector3 oilPos)
    {
        Vector3 capPos = GetBottleCapPosition(bottle);
        Vector3 delta = oilPos - capPos;
        
        bottle.transform.position += delta;
        float minCapY = oilPos.y + minNeckHeightAboveOilHole;
        
        capPos = GetBottleCapPosition(bottle);
        if (capPos.y < minCapY)
        {
            float correction = minCapY - capPos.y;
            bottle.transform.position += Vector3.up * correction;
        }

        return true;
    }

    private Vector3 GetBottleCapPosition(GameObject bottle)
    {
        Transform cap = bottle.transform.Find("Cap") ?? bottle.transform.Find("NozzlePoint");
        if (cap != null) return cap.position;
        return bottle.transform.position + bottle.transform.up * bottleCapHeight;
    }

    private Vector3 GetOilTankPosition()
    {
        return oilTankHole != null ? oilTankHole.position : engine.transform.position + new Vector3(0, 0.3f, 0);
    }

    private IEnumerator RiseEngineForInspection(GameObject targetEngine, float heightOffset, float duration)
    {
        if (!targetEngine) yield break;

        Vector3 startPos = targetEngine.transform.position;
        Vector3 targetPos = startPos + Vector3.up * heightOffset;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t); 
            targetEngine.transform.position = Vector3.Lerp(startPos, targetPos, t);
            yield return null;
        }
        targetEngine.transform.position = targetPos;
    }

    private IEnumerator ReturnBottleToOrigin(GameObject bottle, Vector3 originalPos, Quaternion originalRot, float duration)
    {
        if (!bottle) yield break;

        Vector3 startPos = bottle.transform.position;
        Quaternion startRot = bottle.transform.rotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            t = t * t * (3f - 2f * t); 
            
            bottle.transform.position = Vector3.Lerp(startPos, originalPos, t);
            bottle.transform.rotation = Quaternion.Slerp(startRot, originalRot, t);
            yield return null;
        }
        
        bottle.transform.position = originalPos;
        bottle.transform.rotation = originalRot;
        
        if (activeGrabInteractable != null)
        {
            activeGrabInteractable.enabled = true;
            Rigidbody rb = bottle.GetComponent<Rigidbody>();
            if (rb) 
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true; 
            }
        }
    }

    #endregion

    #region === ENGINE VFX & AUDIO EFFECTS ===

    private void ApplyBPCLEffect()
    {
        StartCoroutine(FadeEngineOuterBodies(engineBodyTransparentAlpha, engineBodyFadeDuration));
        StartCoroutine(FadeEngineGlowEffect(bpclGlowColor, true, engineGlowTransitionDuration));
        PlayOilBubbleParticles(new Color(0.9f, 0.8f, 0.1f, 1f)); 
        PlayEngineSmokeParticles(new Color32(255, 255, 255, 10));
        SetAnimatorTrigger("PerformanceBoost");
    }

    private void ApplyLocalOilEffect()
    {
        StartCoroutine(FadeEngineOuterBodies(engineBodyTransparentAlpha, engineBodyFadeDuration));
        StartCoroutine(FadeEngineGlowEffect(normalOilDarkColor, false, engineGlowTransitionDuration));
        PlayEngineSmokeParticles(new Color32(77, 77, 77, 50));
        StartCoroutine(ShakeEngineRoutine(3.0f, 0.05f));
        SetAnimatorTrigger("PerformanceDrop");
    }

    private void SetAnimatorTrigger(string triggerName)
    {
        if (engine == null) return;
        Animator animator = engine.GetComponentInChildren<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.name == triggerName && p.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(triggerName);
                    return;
                }
            }
        }
    }
    
    private IEnumerator ShakeEngineRoutine(float duration, float maxIntensity)
    {
        if (engine == null) yield break;

        Quaternion origRot = engine.transform.localRotation;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float currentIntensity = maxIntensity * (1f - (elapsed / duration)) * 40f;
            
            engine.transform.localRotation = origRot * Quaternion.Euler(
                UnityEngine.Random.Range(-currentIntensity, currentIntensity),
                UnityEngine.Random.Range(-currentIntensity, currentIntensity),
                UnityEngine.Random.Range(-currentIntensity, currentIntensity)
            );
            yield return null;
        }
        engine.transform.localRotation = origRot;
    }

    private IEnumerator FadeEngineGlowEffect(Color targetColor, bool enableEmission, float duration)
    {
        if (engine == null || engineGlowMaterial == null) yield break;

        Renderer[] renderers = engine.GetComponentsInChildren<Renderer>(true);
        List<(Material mat, Color startColor, Color startEmis)> matsToFade = new List<(Material, Color, Color)>();

        foreach (Renderer r in renderers)
        {
            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                Material sharedMat = r.sharedMaterials[i];
                if (sharedMat != null && (sharedMat == engineGlowMaterial || sharedMat.name.StartsWith(engineGlowMaterial.name)))
                {
                    Material instanceMat = r.materials[i];
                    Color sCol = instanceMat.HasProperty("_BaseColor") ? instanceMat.GetColor("_BaseColor") : instanceMat.color;
                    Color sEmis = instanceMat.HasProperty("_EmissionColor") ? instanceMat.GetColor("_EmissionColor") : Color.black;
                    
                    if (enableEmission) instanceMat.EnableKeyword("_EMISSION");
                    matsToFade.Add((instanceMat, sCol, sEmis));
                }
            }
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            foreach (var fade in matsToFade)
            {
                Color curCol = Color.Lerp(fade.startColor, targetColor, t);
                Color curEmis = Color.Lerp(fade.startEmis, enableEmission ? targetColor : Color.black, t);
                if (fade.mat.HasProperty("_BaseColor")) fade.mat.SetColor("_BaseColor", curCol);
                else fade.mat.color = curCol;
                if (fade.mat.HasProperty("_EmissionColor")) fade.mat.SetColor("_EmissionColor", curEmis);
            }
            yield return null;
        }
    }

    private IEnumerator FadeEngineOuterBodies(float targetAlpha, float duration)
    {
        if (engine == null || engineTransparentMaterials.Count == 0) yield break;

        Renderer[] renderers = engine.GetComponentsInChildren<Renderer>(true);
        List<(Material mat, string prop, Color startColor)> matsToFade = new List<(Material, string, Color)>();

        foreach (Renderer r in renderers)
        {
            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                Material sharedMat = r.sharedMaterials[i];
                if (sharedMat != null && engineTransparentMaterials.Exists(m => m != null && sharedMat.name.StartsWith(m.name)))
                {
                    Material instanceMat = r.materials[i];
                    string colorProp = instanceMat.HasProperty("_BaseColor") ? "_BaseColor" : (instanceMat.HasProperty("_Color") ? "_Color" : string.Empty);
                    if (!string.IsNullOrEmpty(colorProp))
                    {
                        if (instanceMat.HasProperty("_Surface")) instanceMat.SetFloat("_Surface", 1f); 
                        instanceMat.SetOverrideTag("RenderType", "Transparent");
                        instanceMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        instanceMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        instanceMat.SetInt("_ZWrite", 0);
                        instanceMat.DisableKeyword("_ALPHATEST_ON");
                        instanceMat.EnableKeyword("_ALPHABLEND_ON");
                        instanceMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        instanceMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                        
                        matsToFade.Add((instanceMat, colorProp, instanceMat.GetColor(colorProp)));
                    }
                }
            }
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            foreach (var fade in matsToFade)
            {
                Color c = fade.startColor;
                c.a = Mathf.Lerp(fade.startColor.a, targetAlpha, t);
                fade.mat.SetColor(fade.prop, c);
            }
            yield return null;
        }
    }
    
    private void UpdatePourParticles(GameObject bottle)
    {
        if (bottle == null) return;
        
        if (pouringParticles == null)
        {
            ParticleSystem[] pSystems = bottle.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in pSystems)
            {
                if (ps.gameObject.name.Contains("Pouring"))
                {
                    pouringParticles = ps;
                    break;
                }
            }
        }

        if (pouringParticles == null) return;

        pouringParticles.gameObject.SetActive(true);

        Vector3 capPos = GetBottleCapPosition(bottle);
        pouringParticles.transform.position = capPos;
        
        Vector3 toTarget = GetOilTankPosition() - capPos;
        Vector3 dir = toTarget.sqrMagnitude > 0.001f ? toTarget.normalized : Vector3.down;
        pouringParticles.transform.rotation = Quaternion.LookRotation(dir);

        if (!pouringParticles.isPlaying) pouringParticles.Play(true);
    }

    private void StopPourParticles()
    {
        if (pouringParticles) 
        {
            pouringParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            pouringParticles = null; 
        }
    }

    private void FindEngineSmokeParticles()
    {
        if (engine == null) return;
        engineSmokeParticles.Clear();

        ParticleSystem[] allParticles = engine.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in allParticles)
        {
            if (ps.name.Contains("Smoke"))
            {
                engineSmokeParticles.Add(ps);
                if (!engineSmokeDefaultStartColors.ContainsKey(ps))
                {
                    engineSmokeDefaultStartColors[ps] = ps.main.startColor;
                }
            }
        }
    }

    private void PlayEngineSmokeParticles(Color? customColor = null, float? customAlpha = null)
    {
        foreach (var ps in engineSmokeParticles)
        {
            if (ps == null) continue;
            
            ps.gameObject.SetActive(true); 
            
            var mainState = ps.main;
            if (customColor.HasValue)
            {
                Color c = customColor.Value;
                if (customAlpha.HasValue) c.a = customAlpha.Value;
                mainState.startColor = new ParticleSystem.MinMaxGradient(c); 
            }
            if (!ps.isPlaying) ps.Play(true);
        }
    }

    private void StopEngineSmokeParticles()
    {
        foreach (var ps in engineSmokeParticles)
        {
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void FindOilBubbleParticles()
    {
        if (engine == null) return;
        engineOilBubbleParticles.Clear();

        ParticleSystem[] allParticles = engine.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in allParticles)
        {
            if (ps.gameObject.name.Contains("OilBubble"))
            {
                engineOilBubbleParticles.Add(ps);
            }
        }
    }

    private void PlayOilBubbleParticles(Color targetColor)
    {
        foreach (var ps in engineOilBubbleParticles)
        {
            if (ps == null) continue;
            
            ps.gameObject.SetActive(true);
            var mainState = ps.main;
            mainState.startColor = new ParticleSystem.MinMaxGradient(targetColor);
            
            if (!ps.isPlaying) ps.Play(true);
        }
    }

    private void StopOilBubbleParticles()
    {
        foreach (var ps in engineOilBubbleParticles)
        {
            if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    #endregion

    #region === AUDIO UTILS ===

    private IEnumerator ResetFailedStateAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        TransitionToState(InteractionState.Idle);
    }

    private void SetEngineLoopClip(AudioClip clip)
    {
        activeEngineSource.clip = clip;
        if (clip) activeEngineSource.Play();
        else activeEngineSource.Stop();
    }

    private void CrossfadeEngineLoopClip(AudioClip clip, float duration)
    {
        if (audioCrossfadeCoroutine != null) StopCoroutine(audioCrossfadeCoroutine);
        if (clip == null || activeEngineSource.clip == clip) return;

        audioCrossfadeCoroutine = StartCoroutine(CrossfadeAudioRoutine(clip, duration));
    }

    private IEnumerator CrossfadeAudioRoutine(AudioClip targetClip, float duration)
    {
        AudioSource fadingOut = activeEngineSource;
        AudioSource fadingIn = activeEngineSource == engineAudioSource1 ? engineAudioSource2 : engineAudioSource1;

        fadingIn.clip = targetClip;
        fadingIn.loop = true;
        fadingIn.volume = 0f;
        fadingIn.Play();

        activeEngineSource = fadingIn;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            fadingIn.volume = Mathf.Lerp(0, engineLoopVolume, t);
            fadingOut.volume = Mathf.Lerp(engineLoopVolume, 0, t);
            yield return null;
        }

        fadingOut.Stop();
        fadingIn.volume = engineLoopVolume;
        audioCrossfadeCoroutine = null;
    }

    #endregion
}
