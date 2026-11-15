import sys

file_content = r"""using System.Collections;
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

    public enum OilType { None, BPCL, Local }
    public enum InteractionState
    {
        Idle,
        Pouring,
        PourComplete,
        PourFailed
    }

    #region === SERIALIZED FIELDS ===

    [Header("=== SCENE REFERENCES ===")]
    public GameObject engine;
    public GameObject bpclBottle;
    public GameObject localBottle;
    public Transform oilTankHole;
    public Camera vrCamera;
    
    [Header("=== VR INTERACTION ===")]
    public UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable bpclGrabInteractable;
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

    [Header("=== AUDIO ===")]
    public AudioClip pouringSound;
    public AudioClip successSound;
    public AudioClip warningSound;
    public AudioClip defaultEngineLoop;
    public AudioClip normalOilEngineLoop;
    public AudioClip bpclOilEngineLoop;
    [Range(0f, 1f)] public float engineLoopVolume = 0.75f;
    public float audioTransitionDuration = 3.5f;

    [Header("=== VR UI ===")]
    public float vrPanelScale = 0.0015f;
    public float panelDistanceFromEngine = 0.4f;
    public float panelHeightOffset = 0.4f;
    public Vector3 panelFacingRotationOffset = new Vector3(0f, 180f, 0f);
    public Vector3 mainCanvasOffset = new Vector3(0f, 0.8f, 0f);
    public Sprite bpclLogoSprite;
    public bool enableGuidedOnboarding = true;
    public bool enablePourScoring = true;

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
    private Slider pourSlider;
    private GameObject resultPanel;
    private TextMeshProUGUI resultText;
    
    private GameObject splashScreenPanel;
    private CanvasGroup splashCanvasGroup;

    private GameObject bottomActivityPanel;
    private TextMeshProUGUI bottomActivityTitleText;
    private TextMeshProUGUI bottomActivityDetailText;
    private TextMeshProUGUI bottomActivityPhaseIconText;
    private Image bottomActivityIndicator;
    private RectTransform bottomActivityIndicatorRect;

    private GameObject vrBenefitsPanel;
    
    // State Tracking
    private bool hasSelectedAnyBottle = false;
    private bool hasCompletedBPCL = false;
    private bool hasCompletedLocal = false;
    private readonly PourScore bpclScore = new PourScore("MAK Premium");
    private readonly PourScore localScore = new PourScore("Local Oil");
    private Coroutine sequentialInstructionCoroutine;

    private Vector3 engineInitialPosition;
    private Quaternion engineInitialRotation;

    private Vector3 bpclDefaultPos = new Vector3(-5.69099998f, 1.06299996f, -4.75400019f);
    private Vector3 localDefaultPos = new Vector3(-5.0079999f, 1.07099998f, -4.75f);
    private Quaternion bpclDefaultRot;
    private Quaternion localDefaultRot;

    #endregion

    #region === LIFECYCLE ===

    private void Awake()
    {
        EnsureConvexColliders(bpclBottle);
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
        
        if (bpclBottle != null) bpclDefaultRot = bpclBottle.transform.rotation;
        if (localBottle != null) localDefaultRot = localBottle.transform.rotation;

        CreateUI();
        StartCoroutine(ShowSplashScreen());

        SetEngineLoopClip(defaultEngineLoop);
        UpdateStatus("Ready");
        ShowPourSlider(false);
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

    #endregion

    #region === UI CREATION & LOGIC ===

    private IEnumerator ShowSplashScreen()
    {
        if (splashScreenPanel != null)
        {
            splashScreenPanel.SetActive(true);
            yield return new WaitForSeconds(4f);
            splashScreenPanel.SetActive(false);
        }
        
        UpdateStatus("●  Ready");
        sequentialInstructionCoroutine = StartCoroutine(ShowSequentialInstructions(
            "Select an oil",
            "Grab a bottle to begin"
        ));
    }

    private void CreateUI()
    {
        // Create Canvas in World Space
        GameObject canvasObj = new GameObject("VR_Oil_Canvas");
        uiCanvas = canvasObj.AddComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.WorldSpace;
        uiCanvas.sortingOrder = 100;
        
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
        
        // === TOP STATUS BAR ===
        GameObject topBar = CreateMinimalPanel(canvasObj.transform, "TopBar",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -80), new Vector2(0, 140), 
            new Color(0.08f, 0.1f, 0.15f, 0.95f));
            
        GameObject topBarBorder = new GameObject("TopBarBorder");
        topBarBorder.transform.SetParent(topBar.transform, false);
        RectTransform tbBorderRect = topBarBorder.AddComponent<RectTransform>();
        tbBorderRect.anchorMin = new Vector2(0, 0);
        tbBorderRect.anchorMax = new Vector2(1, 0);
        tbBorderRect.pivot = new Vector2(0.5f, 0);
        tbBorderRect.anchoredPosition = Vector2.zero;
        tbBorderRect.sizeDelta = new Vector2(0, 2f);
        Image tbBorderImg = topBarBorder.AddComponent<Image>();
        tbBorderImg.color = new Color(0.3f, 0.5f, 0.8f, 0.5f);

        statusText = CreateMinimalText(topBar.transform, "StatusText", 
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -55), new Vector2(800, 50),
            "●  Initializing", 34, TextAlignmentOptions.Center,
            new Color(1f, 0.98f, 0.95f, 1f));
        statusText.fontStyle = FontStyles.Bold;
        
        UnityEngine.UI.Shadow stShadow = statusText.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        stShadow.effectColor = new Color(0, 0, 0, 0.6f);
        stShadow.effectDistance = new Vector2(1, -1);
        
        instructionText = CreateMinimalText(topBar.transform, "InstructionText",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -100), new Vector2(900, 40),
            "", 24, TextAlignmentOptions.Center,
            new Color(0.8f, 0.9f, 1f, 1f));
            
        UnityEngine.UI.Outline instOutline = instructionText.gameObject.AddComponent<UnityEngine.UI.Outline>();
        instOutline.effectColor = new Color(0, 0, 0, 0.7f);
        instOutline.effectDistance = new Vector2(1, -1);
        
        // === POUR PROGRESS ===
        GameObject pourContainer = new GameObject("PourContainer");
        pourContainer.transform.SetParent(canvasObj.transform, false);
        RectTransform pourContainerRect = pourContainer.AddComponent<RectTransform>();
        pourContainerRect.anchorMin = new Vector2(0.5f, 0);
        pourContainerRect.anchorMax = new Vector2(0.5f, 0);
        pourContainerRect.pivot = new Vector2(0.5f, 0);
        pourContainerRect.anchoredPosition = new Vector2(0, 200); 
        pourContainerRect.sizeDelta = new Vector2(320, 50);
        
        pourSlider = CreateMinimalSlider(pourContainer.transform, "PourSlider",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        pourContainer.SetActive(false);

        bottomActivityPanel = CreateGlassPanel(canvasObj.transform, "BottomActivityPanel",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 80f), new Vector2(480f, 90f),
            new Color(0.04f, 0.06f, 0.09f, 0.92f));
        AddPanelBorder(bottomActivityPanel, new Color(0.25f, 0.7f, 0.55f, 0.45f), 1.5f);

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
            new Color(0.62f, 0.82f, 0.75f, 0.96f));

        bottomActivityPanel.SetActive(false);
        pourSlider.transform.parent.gameObject.SetActive(false);
        
        // === RESULT PANEL ===
        resultPanel = CreateGlassPanel(canvasObj.transform, "ResultPanel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 120), new Vector2(380, 100), 
            new Color(0.08f, 0.1f, 0.12f, 0.92f));
        
        AddPanelBorder(resultPanel, new Color(0.3f, 0.4f, 0.5f, 0.4f), 1.5f);
        
        resultText = CreateMinimalText(resultPanel.transform, "ResultText",
            new Vector2(0, 0), new Vector2(1, 1),
            Vector2.zero, new Vector2(-30, -20),
            "", 22, TextAlignmentOptions.Center,
            Color.white);
        resultText.fontStyle = FontStyles.Bold;
        resultPanel.SetActive(false);
        
        CreateSplashScreenUI(canvasRect);

        Debug.Log("[VR_Oil] Programmatic UI created");
    }

    private void CreateSplashScreenUI(RectTransform canvasRect)
    {
        splashScreenPanel = new GameObject("SplashScreenPanel");
        splashScreenPanel.transform.SetParent(canvasRect.transform, false);
        
        RectTransform splashRect = splashScreenPanel.AddComponent<RectTransform>();
        splashRect.anchorMin = Vector2.zero;
        splashRect.anchorMax = Vector2.one;
        splashRect.sizeDelta = Vector2.zero;
        splashRect.anchoredPosition = Vector2.zero;
        
        Image bgImg = splashScreenPanel.AddComponent<Image>();
        bgImg.color = new Color(0.95f, 0.95f, 0.95f, 1f); 
        
        splashCanvasGroup = splashScreenPanel.AddComponent<CanvasGroup>();
        splashCanvasGroup.alpha = 1f;
        
        VerticalLayoutGroup layout = splashScreenPanel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.spacing = 60f;
        
        GameObject logoObj = new GameObject("BPCL_Logo");
        logoObj.transform.SetParent(splashScreenPanel.transform, false);
        Image logoImg = logoObj.AddComponent<Image>();
        
        if (bpclLogoSprite != null)
        {
            logoImg.sprite = bpclLogoSprite;
            logoImg.preserveAspect = true;
        }
        else
        {
            logoImg.color = Color.blue;
        }
        
        RectTransform logoRect = logoObj.GetComponent<RectTransform>();
        logoRect.sizeDelta = new Vector2(400, 400); 

        GameObject titleObj = new GameObject("WelcomeTitle");
        titleObj.transform.SetParent(splashScreenPanel.transform, false);
        TextMeshProUGUI titleTxt = titleObj.AddComponent<TextMeshProUGUI>();
        titleTxt.text = "VR Engine Oil Comparison";
        titleTxt.fontSize = 60;
        titleTxt.fontStyle = FontStyles.Bold;
        titleTxt.alignment = TextAlignmentOptions.Center;
        titleTxt.color = new Color(0.1f, 0.2f, 0.5f, 1f); 
        
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.sizeDelta = new Vector2(700, 150); 
        
        GameObject descObj = new GameObject("DescriptionText");
        descObj.transform.SetParent(splashScreenPanel.transform, false);
        TextMeshProUGUI descTxt = descObj.AddComponent<TextMeshProUGUI>();
        descTxt.text = "Experience the difference in engine protection\nand performance.\n\n• Advanced Wear Protection\n• Superior Heat Resistance\n• Enhanced Engine Life";
        descTxt.fontSize = 36;
        descTxt.alignment = TextAlignmentOptions.Center;
        descTxt.color = new Color(0.3f, 0.3f, 0.3f, 1f); 
        
        RectTransform descRect = descObj.GetComponent<RectTransform>();
        descRect.sizeDelta = new Vector2(800, 300);

        splashScreenPanel.SetActive(true);
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
        img.raycastTarget = false;
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
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        
        return tmp;
    }

    private Slider CreateMinimalSlider(Transform parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 sizeDelta)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        
        GameObject bg = new GameObject("Track");
        bg.transform.SetParent(obj.transform, false);
        RectTransform bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.3f);
        bgRect.anchorMax = new Vector2(1, 0.7f);
        bgRect.offsetMin = new Vector2(8, 0);
        bgRect.offsetMax = new Vector2(-8, 0);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.15f, 0.18f, 0.22f, 0.9f);
        
        GameObject fillArea = new GameObject("FillArea");
        fillArea.transform.SetParent(obj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.3f);
        fillAreaRect.anchorMax = new Vector2(1, 0.7f);
        fillAreaRect.offsetMin = new Vector2(8, 2);
        fillAreaRect.offsetMax = new Vector2(-8, -2);
        
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0, 1);
        fillRect.pivot = new Vector2(0, 0.5f);
        fillRect.sizeDelta = Vector2.zero;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.85f, 0.5f, 1f); 
        
        GameObject label = new GameObject("Label");
        label.transform.SetParent(obj.transform, false);
        RectTransform labelRect = label.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = new Vector2(200, 30);
        
        TextMeshProUGUI labelText = label.AddComponent<TextMeshProUGUI>();
        labelText.text = "POURING";
        labelText.fontSize = 14;
        labelText.alignment = TextAlignmentOptions.Center;
        labelText.color = new Color(0.8f, 0.85f, 0.9f, 0.9f);
        labelText.fontStyle = FontStyles.Bold;
        labelText.characterSpacing = 4f;
        
        Slider slider = obj.AddComponent<Slider>();
        slider.fillRect = fillRect;
        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = 0;
        slider.interactable = false;
        
        return slider;
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
    
    private void ShowPourSlider(bool show)
    {
        if (pourSlider != null && pourSlider.transform.parent != null)
        {
            pourSlider.transform.parent.gameObject.SetActive(show);
            if (show) pourSlider.value = 0;
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
            return "Step 3/3 See results and try the other";

        if (hasCompletedBPCL && hasCompletedLocal)
            return "Comparison ready. Grab any bottle to retry";

        if (hasCompletedBPCL || hasCompletedLocal)
            return "Grab the other bottle to compare";

        return "Grab a bottle to begin";
    }

    #endregion

    #region === BENEFITS PANEL ===

    private void ShowBPCLBenefits()
    {
        string benefits = "<b>MAK Supreme</b>\n" +
"<size=80%><color=#4ADE80>Synthetic</color></size>\n\n" +
"<color=#86EFAC>✓</color> More Mileage\n" +
"<color=#86EFAC>✓</color> Long Life\n" +
"<color=#86EFAC>✓</color> All Weather\n" +
"<color=#86EFAC>✓</color> Smooth Start";

        CreateVRBenefitsPanel(benefits, new Color(0.08f, 0.15f, 0.12f, 0.95f), true);
    }
    
    private void ShowLocalOilDemerits()
    {
        string demerits = "<b>Mineral Lube</b>\n" +
"<size=80%><color=#F87171>Standard</color></size>\n\n" +
"<color=#FCA5A5>✗</color> Less Mileage\n" +
"<color=#FCA5A5>✗</color> Short Life\n" +
"<color=#FCA5A5>✗</color> Limited Use\n" +
"<color=#FCA5A5>✗</color> Slow Start";

        CreateVRBenefitsPanel(demerits, new Color(0.18f, 0.08f, 0.08f, 0.95f), false);
    }
    
    private void HideVRBenefitsPanel()
    {
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
        Vector3 toCamera = vrCamera != null ? vrCamera.transform.position - enginePos : -Vector3.forward;
        toCamera.y = 0; 
        toCamera.Normalize();
        
        Vector3 rightDir = Vector3.Cross(Vector3.up, toCamera).normalized;
        Vector3 panelWorldPos = enginePos 
            + rightDir * panelDistanceFromEngine 
            + Vector3.up * panelHeightOffset;
        
        vrBenefitsPanel.transform.position = panelWorldPos;
        
        Vector3 lookDir = vrCamera != null ? vrCamera.transform.position - panelWorldPos : -Vector3.forward;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            vrBenefitsPanel.transform.rotation = Quaternion.LookRotation(lookDir.normalized) * Quaternion.Euler(panelFacingRotationOffset);
        }
        
        Canvas canvas = vrBenefitsPanel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        RectTransform canvasRect = vrBenefitsPanel.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(200, 150); 
        
        vrBenefitsPanel.transform.localScale = Vector3.one * vrPanelScale;
        
        CreatePanelBackground(bgColor, isPositive);
        CreatePanelText(text);
        
        Debug.Log($"[VR_Oil] VR Benefits panel created - Position: {panelWorldPos}, Scale: {vrPanelScale}");
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
            ? new Color(0.3f, 0.7f, 0.4f, 0.6f)
            : new Color(0.7f, 0.3f, 0.3f, 0.6f);
        
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
        textRect.offsetMin = new Vector2(10, 8);
        textRect.offsetMax = new Vector2(-10, -12);
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 12;
        tmp.color = new Color(0.95f, 0.97f, 1f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.lineSpacing = -5f;
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }

    #endregion

    #region === AUDIO SETUP ===

    private void InitializeAudio()
    {
        sfxAudioSource = gameObject.AddComponent<AudioSource>();
        sfxAudioSource.playOnAwake = false;

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
        if (bpclGrabInteractable)
        {
            bpclGrabInteractable.selectEntered.AddListener((args) => OnBottleGrabbed(OilType.BPCL, bpclBottle, bpclGrabInteractable));
            bpclGrabInteractable.selectExited.AddListener((args) => OnBottleReleased());
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
            currentState = InteractionState.Idle;
            UpdateInstructions(GetGuidedInstruction(currentState));
        }

        if (currentState != InteractionState.Idle && currentState != InteractionState.PourFailed) return;

        hasSelectedAnyBottle = true;
        isHoldingBottle = true;
        heldOilType = oilType;
        heldBottle = bottle;
        heldGrabInteractable = grabInteractable;

        string bottleName = oilType == OilType.BPCL ? "MAK Premium" : "Local Oil";
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

        float progress = Mathf.Clamp01((pourElapsedTime - (approach + tilt)) / pourHoldDuration);
        if (pourSlider) pourSlider.value = Mathf.Max(0, progress);

        if (pourElapsedTime >= totalDuration)
        {
            CompletePour();
        }
    }

    private void CompletePour()
    {
        if (successSound) sfxAudioSource.PlayOneShot(successSound);

        ShowUIMessage(activeBottleType == OilType.BPCL ? "Excellent! MAK Premium applied." : "Local oil applied.");

        if (activeBottleType == OilType.BPCL) hasCompletedBPCL = true;
        else hasCompletedLocal = true;

        Vector3 returnPos = (activeBottleType == OilType.BPCL) ? bpclDefaultPos : localDefaultPos;
        Quaternion returnRot = (activeBottleType == OilType.BPCL) ? bpclDefaultRot : localDefaultRot;

        if (activeBottleType == OilType.BPCL)
        {
            ApplyBPCLEffect();
            ShowBPCLBenefits();
        }
        else
        {
            ApplyLocalOilEffect();
            ShowLocalOilDemerits();
        }

        StartCoroutine(RiseEngineForInspection(engine, postPourRiseHeight, postPourRiseDuration));
        StartCoroutine(ReturnBottleToOrigin(activeBottle, returnPos, returnRot, postPourRiseDuration));
        
        TransitionToState(InteractionState.PourComplete);
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
                ShowPourSlider(false);
                break;

            case InteractionState.Pouring:
                UpdateStatus("▼  Pouring");
                UpdateInstructions(GetGuidedInstruction(newState));
                SetBottomActivityState("POURING", "Aligning bottle to oil inlet", "<color=#86EFAC>◎</color>");
                ShowPourSlider(true);
                if (pourSlider) pourSlider.value = 0;
                
                if (pouringSound) sfxAudioSource.PlayOneShot(pouringSound);
                
                AudioClip targetEngineClip = (activeBottleType == OilType.BPCL) ? bpclOilEngineLoop : normalOilEngineLoop;
                CrossfadeEngineLoopClip(targetEngineClip, audioTransitionDuration); 
                
                Transform nozzle = activeBottle.transform.Find("NozzlePoint");
                if (oilVFXController) oilVFXController.OnPourStart(activeBottleType == OilType.BPCL ? 0 : 1, nozzle ? nozzle : activeBottle.transform);
                if (oilPourManager) oilPourManager.OnPourStart(activeBottleType == OilType.BPCL ? 0 : 1, activeBottle);
                break;
                
            case InteractionState.PourComplete:
                UpdateStatus("✓  Complete");
                UpdateInstructions(GetGuidedInstruction(newState));
                if (oilVFXController) oilVFXController.OnPourComplete();
                if (oilPourManager) oilPourManager.OnPourComplete();
                break;

            case InteractionState.PourFailed:
                UpdateStatus("⚠  Failed");
                CrossfadeEngineLoopClip(defaultEngineLoop, 1.5f);
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
        PlayEngineSmokeParticles(new Color(0.9f, 0.85f, 0.6f), 0.3f);
        SetAnimatorTrigger("PerformanceBoost");
    }

    private void ApplyLocalOilEffect()
    {
        StartCoroutine(FadeEngineOuterBodies(engineBodyTransparentAlpha, engineBodyFadeDuration));
        StartCoroutine(FadeEngineGlowEffect(normalOilDarkColor, false, engineGlowTransitionDuration));
        PlayEngineSmokeParticles(new Color(0.2f, 0.2f, 0.2f), 0.8f);
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
"""

with open(r"d:\Unity Data Clocean\VR_OIL_SIMULATION\update_script.py", "w", encoding="utf-8") as f:
    f.write(f'''import sys

path = r"d:\\Unity Data Clocean\\VR_OIL_SIMULATION\\Assets\\SCRIPT\\VR_OilInteraction.cs"
with open(path, "w", encoding="utf-8") as dest:
    dest.write("""{file_content}""")
''')

print("Created python update script.")
