using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
#endif

/// <summary>
/// AR Oil Demo - Clean Architecture Implementation
/// 
/// Features:
/// - State machine for clear interaction flow
/// - Tap to select/hold bottle (feeling of holding bottle, not phone)
/// - Tilt phone to pour (realistic gravity simulation)
/// - Distance check (too far = "Wrong Pouring" alert)
/// - Tap selected bottle to deselect
/// - Select different bottle to try again (auto-resets engine)
/// 
/// Architecture: SOLID + State Machine + Event-driven
/// </summary>
public class AR_OilInteraction : MonoBehaviour
{
    private sealed class PourScore
    {
        public readonly string Label;
        public bool Completed;
        public float DurationSeconds;
        public int StabilityScore;
        public int ControlScore;
        public int TimeScore;
        public int FinalScore;
        public int TiltDrops;

        public PourScore(string label)
        {
            Label = label;
        }
    }

    #region === ENUMS & STATE ===
    
    public enum OilType { None, BPCL, Local }
    
    public enum InteractionState
    {
        Initializing,       // AR not ready
        WaitingForPlane,    // Scanning for planes
        Spawning,           // Spawning objects
        Idle,               // Objects spawned, waiting for selection
        Holding,            // User is "holding" a bottle
        Pouring,            // Actively pouring
        PourComplete,       // Pour finished, showing results
        PourFailed          // Pour failed (too far, etc.)
    }
    
    #endregion

    #region === SERIALIZED FIELDS ===
    
    [Header("=== PREFABS (Required) ===")]
    [SerializeField] private GameObject enginePrefab;
    [SerializeField] private GameObject bpclBottlePrefab;
    [SerializeField] private GameObject localBottlePrefab;
    
    [Header("=== AR REFERENCES ===")]
    [SerializeField] private ARPlaneManager arPlaneManager;
    [SerializeField] private ARRaycastManager arRaycastManager;
    [SerializeField] private Camera arCamera;
    
    [Header("=== SPAWN SETTINGS ===")]
    [SerializeField] private float spawnDistance = 1.2f;
    [SerializeField] private float engineScale = 0.4f;
    [SerializeField] private float bottleScale = 1.5f; // Increased from 1.0 to 1.5 for better visibility
    [Tooltip("Distance from engine CENTER to bottle. Should be > half engine width")]
    [SerializeField] private float bottleSpacing = 0.25f; // Decreased to bring bottles closer to engine
    [SerializeField] private float engineHeightOffset = 0f;
    [SerializeField] private float bottleHeightOffset = 0.05f; // Bottles slightly above ground
    [Tooltip("Additional forward offset for bottles (negative = behind engine)")]
    [SerializeField] private float bottleForwardOffset = 0.15f; // Bottles slightly in front
    
    [Tooltip("Rotation offset for engine prefab (X, Y, Z degrees)")]
    [SerializeField] private Vector3 engineRotationOffset = new Vector3(180f, 0f, 0f);
    
    [Header("=== TILT DETECTION ===")]
    [Tooltip("How much tilt required to START pouring. Higher = more tilt needed. Range: 0.5-0.8")]
    [SerializeField] [Range(0.5f, 0.9f)] private float tiltStartThreshold = 0.65f;
    
    [Tooltip("How much tilt required to CONTINUE pouring. Slightly less than start.")]
    [SerializeField] [Range(0.4f, 0.8f)] private float tiltContinueThreshold = 0.55f;
    
    [Tooltip("Seconds to complete pour at full tilt")]
    [SerializeField] private float pourDuration = 3.0f;

    [Header("=== POUR PHASE TIMING ===")]
    [Tooltip("Seconds for bottle to move quickly to engine")]
    [SerializeField] private float bottleApproachDuration = 0.6f;
    [Tooltip("Seconds for bottle to rotate into pouring pose")]
    [SerializeField] private float bottleTiltDuration = 0.8f;
    [Tooltip("Seconds to keep pouring once tilted (set 5-10 for longer pour)")]
    [SerializeField] private float pourHoldDuration = 7.0f;
    [Tooltip("Target tilt angle during pour")]
    [SerializeField] private float pourTiltAngle = 120f;
    [Tooltip("Bottle center height above oil hole while pouring")]
    [SerializeField] private float pourTargetHeightOffset = 0.28f;
    [Tooltip("Minimum vertical clearance from bottle neck to oil hole")]
    [SerializeField] private float minNeckHeightAboveOilHole = 0.12f;
    
    [Tooltip("Accelerometer smoothing (0.1=smooth, 0.4=responsive)")]
    [SerializeField] [Range(0.05f, 0.5f)] private float accelSmoothing = 0.15f;
    
    [Header("=== DISTANCE CHECK ===")]
    [Tooltip("Max distance from engine while pouring (meters)")]
    [SerializeField] private float maxPourDistance = 2.5f;
    
    [Header("=== VISUAL FEEDBACK ===")]
    [SerializeField] private Color selectionHighlightColor = new Color(0.4f, 0.85f, 0.6f, 1f); // Soft mint green
    
    [Header("=== PARTICLE SYSTEMS (Optional) ===")]
    [SerializeField] private ParticleSystem pouringParticles;

    [Header("VFX Controller")]
    public OilPourVFXController oilVFXController;
    
    [Header("Oil Pour Manager")]
    public OilPourManager oilPourManager;

    [Header("=== AUDIO (Optional) ===")]
    [SerializeField] private AudioClip selectSound;
    [SerializeField] private AudioClip deselectSound;
    [SerializeField] private AudioClip pouringSound;
    [SerializeField] private AudioClip successSound;
    [SerializeField] private AudioClip warningSound;

    [Header("=== ENGINE LOOP AUDIO ===")]
    [Tooltip("Default engine sound before any oil effect")]
    [SerializeField] private AudioClip defaultEngineLoop;
    [Tooltip("Engine sound after normal/local oil pour")]
    [SerializeField] private AudioClip normalOilEngineLoop;
    [Tooltip("Engine sound after BPCL oil pour")]
    [SerializeField] private AudioClip bpclOilEngineLoop;
    [SerializeField] [Range(0f, 1f)] private float engineLoopVolume = 0.75f;
    [Tooltip("Duration in seconds for the audio to transition/crossfade")]
    [SerializeField] [Range(0.5f, 10f)] private float audioTransitionDuration = 3.5f;
    
    [Header("=== OIL TANK TARGET ===")]
    [SerializeField] private Vector3 oilTankOffset = new Vector3(0f, 0.3f, 0f);
    
    [Header("=== BOTTLE CAP (Pour Origin) ===")]
    [Tooltip("Offset from bottle center to cap/tip in local space (Y = up)")]
    [SerializeField] private float bottleCapHeight = 0.15f;
    
    [Header("=== AR BENEFITS PANEL ===")]
    [Tooltip("Scale of the AR panel in world space. 0.001 = 1mm per unit")]
    [SerializeField] private float arPanelScale = 0.0008f;  // Proper AR scale
    [Tooltip("Distance from engine center to panel")]
    [SerializeField] private float panelDistanceFromEngine = 0.3f;  // 30cm from engine
    [Tooltip("Height offset above engine")]
    [SerializeField] private float panelHeightOffset = 0.2f;  // 20cm above engine

    [Header("=== CAMERA FACING ===")]
    [Tooltip("Keep spawned world objects front-facing the AR camera on Y axis")]
    [SerializeField] private bool keepObjectsFacingCamera = true;
    [Tooltip("Keep AR panel text readable by continuously facing the camera")]
    [SerializeField] private bool keepPanelFacingCamera = true;
    [Tooltip("Panel rotation compensation so the visible face points toward the camera")]
    [SerializeField] private Vector3 panelFacingRotationOffset = new Vector3(0f, 180f, 0f);
    [Tooltip("Optional: also rotate engine to face camera (applies engine rotation offset)")]
    [SerializeField] private bool keepEngineFacingCamera = false;

    [Header("=== POST-POUR ENGINE MANIPULATION ===")]
    [Tooltip("Allow pinch/drag manipulation after a successful pour")]
    [SerializeField] private bool enablePostPourEngineManipulation = true;
    [Tooltip("Allow drag rotation around the engine local Y axis")]
    [SerializeField] private bool allowEngineRotationYAxis = true;
    [Tooltip("Allow drag rotation around the engine local X axis")]
    [SerializeField] private bool allowEngineRotationXAxis = true;
    [Tooltip("Degrees of Y-axis rotation per horizontal pixel dragged")]
    [SerializeField] private float engineRotationSensitivityY = 0.25f;
    [Tooltip("Degrees of X-axis rotation per vertical pixel dragged")]
    [SerializeField] private float engineRotationSensitivityX = 0.18f;
    [Tooltip("Pinch sensitivity. Higher value means faster zoom")]
    [SerializeField] private float enginePinchZoomSensitivity = 0.0025f;
    [Tooltip("Minimum engine scale multiplier relative to original spawned scale")]
    [SerializeField] private float minEngineManipulationScale = 0.85f;
    [Tooltip("Maximum engine scale multiplier relative to original spawned scale")]
    [SerializeField] private float maxEngineManipulationScale = 2.2f;

    [Header("=== EXPERIENCE (Onboarding + Score) ===")]
    [Tooltip("Shows step-by-step guidance in the instruction area")]
    [SerializeField] private bool enableGuidedOnboarding = true;
    [Tooltip("Calculates and displays pour quality score")]
    [SerializeField] private bool enablePourScoring = true;

    [Header("=== CAR MODEL FADE ===")]
    [Tooltip("Optional explicit car model root to fade out. If null, parent of spawned engine is used when available.")]
    [SerializeField] private GameObject carModelToFade;
    [Tooltip("Minimum seconds before car starts fading")]
    [SerializeField] private float carFadeDelayMin = 5f;
    [Tooltip("Maximum seconds before car starts fading")]
    [SerializeField] private float carFadeDelayMax = 10f;
    [Tooltip("Fade-out duration in seconds")]
    [SerializeField] private float carFadeDuration = 3f;

    [Header("=== SPLASH SCREEN (Optional) ===")]
    [Tooltip("Assign the BPCL logo sprite here to display on the splash screen.")]
    [SerializeField] private Sprite bpclLogoSprite;

    [Header("=== ENGINE BODY TRANSPARENCY ===")]
    [Tooltip("Materials of the outer shell to fade out and make transparent after BPCL pour. Assign the materials here.")]
    [SerializeField] private List<Material> engineTransparentMaterials = new List<Material>();
    [Tooltip("Target alpha for the empty engine body when it turns transparent")]
    [SerializeField] [Range(0f, 1f)] private float engineBodyTransparentAlpha = 0.2f;
    [Tooltip("Duration of the transparency fade")]
    [SerializeField] private float engineBodyFadeDuration = 2f;

    [Header("=== ENGINE GLOW EFFECTS ===")]
    [Tooltip("The material that will glow golden after BPCL oil, and turn black after Normal oil.")]
    [SerializeField] private Material engineGlowMaterial;
    [Tooltip("Golden color for BPCL oil (enable HDR on color picker for emission)")]
    [ColorUsage(true, true)] [SerializeField] private Color bpclGlowColor = new Color(1f, 0.8f, 0f, 1f) * 2f;
    [Tooltip("Color for normal oil (usually black/dark, no emission)")]
    [ColorUsage(true, true)] [SerializeField] private Color normalOilDarkColor = Color.black;
    [Tooltip("Time it takes to smoothly transition the engine glow materials")]
    [SerializeField] private float engineGlowTransitionDuration = 2.0f;
    
    #endregion

    #region === RUNTIME STATE ===
    
    // State machine
    private InteractionState currentState = InteractionState.Initializing;

    // UI References (auto-created)
    private GameObject splashScreenPanel;
    private CanvasGroup splashCanvasGroup;
    
    // Spawned objects
    private GameObject spawnedEngine;
    private Transform spawnedPersistentEngineRoot;
    private GameObject spawnedBPCLBottle;
    private GameObject spawnedLocalBottle;
    private bool hasSpawned = false;
    
    // Selection
    private OilType selectedOil = OilType.None;
    private OilType lastPouredOil = OilType.None;
    
    // Bottle state
    private Vector3 selectedBottleOriginalPos;
    private Quaternion selectedBottleOriginalRot;
    private Quaternion selectedBottlePourBaseRot;
    private Dictionary<Renderer, Color[]> originalColors = new Dictionary<Renderer, Color[]>();
    
    // Pour progress
    private float pourProgress = 0f;
    private float pourElapsedTime = 0f;
    private float smoothedTiltValue = 0f;
    
    // Oil tank hole position
    private Transform oilTankHole;
    
    // Engine smoke particles (found in prefab)
    private List<ParticleSystem> engineSmokeParticles = new List<ParticleSystem>();
    private readonly Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient> engineSmokeDefaultStartColors = new Dictionary<ParticleSystem, ParticleSystem.MinMaxGradient>();
    private readonly Dictionary<OilType, List<ParticleSystem>> bottlePourParticles = new Dictionary<OilType, List<ParticleSystem>>();
    private readonly List<ParticleSystem> emptyBottleParticleList = new List<ParticleSystem>();
    
    // AR panel
    private GameObject arBenefitsPanel;
    
    // UI References (auto-created)
    private Canvas uiCanvas;
    private TextMeshProUGUI statusText;
    private TextMeshProUGUI instructionText;
    private Slider pourSlider;
    private GameObject resultPanel;
    private TextMeshProUGUI resultText;
    private GameObject bottomActivityPanel;
    private TextMeshProUGUI bottomActivityTitleText;
    private TextMeshProUGUI bottomActivityDetailText;
    private TextMeshProUGUI bottomActivityPhaseIconText;
    private Image bottomActivityIndicator;
    private RectTransform bottomActivityIndicatorRect;
    
    private AudioSource audioSource;
    private AudioSource engineAudioSource;
    private AudioSource engineAudioSource2;
    private AudioSource activeEngineSource;
    private Coroutine audioCrossfadeCoroutine;
    
    // Debug
    private int frameCounter = 0;
    private bool isBottleReturnAnimating = false;
    private Coroutine carFadeCoroutine;
    private float nextVibratePulseTime = 0f;
    private Animator cachedEngineAnimator;
    private Vector3 spawnedEngineOriginalPosition;
    private Quaternion spawnedEngineOriginalRotation = Quaternion.identity;
    private Vector3 spawnedEngineOriginalScale = Vector3.one;
    private bool hasCachedEngineTransform = false;

    private bool isEngineRotateGestureActive = false;
    private Vector2 engineRotateLastPosition;
    private int engineRotateTouchId = -1;
    private bool isEnginePinchActive = false;
    private float enginePinchStartDistance = 0f;
    private Vector3 enginePinchStartScale = Vector3.one;

    // Onboarding + scoring state
    private bool hasSelectedAnyBottle = false;
    private bool hasCompletedBPCL = false;
    private bool hasCompletedLocal = false;
    private readonly PourScore bpclScore = new PourScore("MAK Premium");
    private readonly PourScore localScore = new PourScore("Local Oil");

    private float activePourStartTime = 0f;
    private float activeTiltErrorAccum = 0f;
    private int activeTiltSamples = 0;
    private int activeTiltDrops = 0;
    private bool activeTiltBelowThreshold = false;

    // Sequential instruction state
    private Coroutine sequentialInstructionCoroutine;
    
    #endregion

    #region === UNITY LIFECYCLE ===
    
    private void Awake()
    {
        InitializeInputSystem();
        InitializeAudioSource();
        ValidateReferences();
    }
    
    private void Start()
    {
        CreateUI();
        CreatePouringParticleSystem();
        StartCoroutine(ShowSplashScreen());
    }
    
    private void Update()
    {
        // Don't run main update loop until splash screen is done
        if (currentState == InteractionState.Initializing) return;

        frameCounter++;
        
        switch (currentState)
        {
            case InteractionState.WaitingForPlane:
                UpdateWaitingForPlane();
                break;
                
            case InteractionState.Idle:
                HandleTouchInput();
                break;
                
            case InteractionState.Holding:
                HandleTouchInput();
                UpdateHoldingState();
                break;
                
            case InteractionState.Pouring:
                UpdatePouringState();
                break;

            case InteractionState.PourComplete:
                HandlePostPourInput();
                break;
        }

        UpdateCameraFacingAlignment();
        UpdateBottomActivityVisualPulse();
        
        // Periodic debug logging
        if (frameCounter % 120 == 0)
        {
            LogDebugState();
        }
    }
    
    private void OnDestroy()
    {
        CleanupResources();
    }

    #endregion

    #region === SPLASH SCREEN ===

    private IEnumerator ShowSplashScreen()
    {
        if (splashScreenPanel != null)
        {
            splashScreenPanel.SetActive(true);
            yield return new WaitForSeconds(4f); // Show for 4 seconds
            splashScreenPanel.SetActive(false);
        }
        
        // Now that splash is done, start the main AR flow
        TransitionToState(InteractionState.WaitingForPlane);
    }

    #endregion

    #region === INITIALIZATION ===
    
    private void InitializeInputSystem()
    {
        #if ENABLE_INPUT_SYSTEM
        // Enable enhanced touch support
        if (!EnhancedTouchSupport.enabled)
        {
            EnhancedTouchSupport.Enable();
        }
        
        // Enable accelerometer
        if (Accelerometer.current != null)
        {
            InputSystem.EnableDevice(Accelerometer.current);
            Debug.Log("[AR_Oil] Accelerometer enabled");
        }
        else if (GravitySensor.current != null)
        {
            InputSystem.EnableDevice(GravitySensor.current);
            Debug.Log("[AR_Oil] GravitySensor enabled (fallback)");
        }
        else
        {
            Debug.LogWarning("[AR_Oil] No accelerometer found!");
        }
        #endif
    }
    
    private void InitializeAudioSource()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;

        engineAudioSource = gameObject.AddComponent<AudioSource>();
        engineAudioSource.playOnAwake = false;
        engineAudioSource.loop = true;
        engineAudioSource.volume = engineLoopVolume;
        engineAudioSource.spatialBlend = 0f;

        engineAudioSource2 = gameObject.AddComponent<AudioSource>();
        engineAudioSource2.playOnAwake = false;
        engineAudioSource2.loop = true;
        engineAudioSource2.volume = 0f;
        engineAudioSource2.spatialBlend = 0f;

        activeEngineSource = engineAudioSource;
    }
    
    private void ValidateReferences()
    {
        // Find AR components if not assigned
        if (arPlaneManager == null)
            arPlaneManager = FindFirstObjectByType<ARPlaneManager>();
            
        if (arRaycastManager == null)
            arRaycastManager = FindFirstObjectByType<ARRaycastManager>();
            
        if (arCamera == null)
            arCamera = Camera.main;
        
        // Validation
        if (enginePrefab == null)
            Debug.LogError("[AR_Oil] Engine Prefab not assigned!");
        if (bpclBottlePrefab == null)
            Debug.LogError("[AR_Oil] BPCL Bottle Prefab not assigned!");
        if (localBottlePrefab == null)
            Debug.LogError("[AR_Oil] Local Bottle Prefab not assigned!");
    }
    
    /// <summary>
    /// Create a realistic oil pouring particle system if none assigned
    /// </summary>
    private void CreatePouringParticleSystem()
    {
        // If already assigned, configure it for oil
        if (pouringParticles != null)
        {
            ConfigureOilParticles(pouringParticles);
            return;
        }
        
        // Create new particle system
        GameObject particleObj = new GameObject("OilPouringParticles");
        particleObj.transform.SetParent(transform);
        particleObj.transform.localPosition = Vector3.zero;
        
        pouringParticles = particleObj.AddComponent<ParticleSystem>();
        
        // Stop to configure
        pouringParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        
        ConfigureOilParticles(pouringParticles);
        
        Debug.Log("[AR_Oil] Created oil pouring particle system");
    }
    
    /// <summary>
    /// Configure particle system to look like realistic oil pour
    /// </summary>
    private void ConfigureOilParticles(ParticleSystem ps)
    {
        // === MAIN MODULE ===
        var main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = 0.8f;
        main.startSpeed = 2.5f;
        main.startSize = 0.015f;
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.5f;  // Oil falls with gravity
        
        // Oil color - dark amber/brown
        main.startColor = new Color(0.15f, 0.1f, 0.02f, 0.95f);  // Dark golden brown oil
        
        // === EMISSION ===
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 150f;  // Continuous stream
        
        // === SHAPE - Cone for pour stream ===
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;  // Narrow stream
        shape.radius = 0.005f;  // Small opening
        shape.radiusThickness = 1f;
        
        // === SIZE OVER LIFETIME - Slight expansion as it falls ===
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.8f);
        sizeCurve.AddKey(0.3f, 1f);
        sizeCurve.AddKey(1f, 1.2f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
        
        // === COLOR OVER LIFETIME - Slight transparency at end ===
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.2f, 0.12f, 0.02f), 0f),    // Dark amber
                new GradientColorKey(new Color(0.25f, 0.15f, 0.03f), 0.5f), // Slightly lighter
                new GradientColorKey(new Color(0.18f, 0.1f, 0.02f), 1f)     // Dark again
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.95f, 0.7f),
                new GradientAlphaKey(0.6f, 1f)  // Fade at end
            }
        );
        colorOverLifetime.color = gradient;
        
        // === VELOCITY OVER LIFETIME - Add slight wobble ===
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        
        // === NOISE - Fluid turbulence ===
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.1f;
        noise.frequency = 2f;
        noise.scrollSpeed = 0.5f;
        noise.damping = true;
        
        // === COLLISION - Splash on engine ===
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.dampen = 0.3f;
        collision.bounce = 0.1f;  // Oil doesn't bounce much
        collision.lifetimeLoss = 0.5f;
        
        // === RENDERER - Use default particle material with proper settings ===
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            
            // Try to use existing default material first, then create custom
            Material oilMaterial = GetOrCreateParticleMaterial(new Color(0.2f, 0.13f, 0.04f, 1f), "OilMaterial");
            renderer.material = oilMaterial;
            
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = 0.001f;
            renderer.maxParticleSize = 0.1f;
        }
        
        Debug.Log("[AR_Oil] Oil particles configured");
    }
    
    /// <summary>
    /// Get or create a particle material with specified color
    /// Uses the most reliable method to avoid pink/missing shader issues
    /// </summary>
    private Material GetOrCreateParticleMaterial(Color color, string name)
    {
        // FIRST: Try to load the default particle material from Resources
        Material defaultParticleMat = Resources.Load<Material>("Default-Particle");
        if (defaultParticleMat != null)
        {
            Material mat = new Material(defaultParticleMat);
            mat.name = name;
            mat.color = color;
            Debug.Log($"[AR_Oil] Using Default-Particle material for {name}");
            return mat;
        }
        
        // SECOND: Try to find and use existing ParticleSystemRenderer's default material
        ParticleSystemRenderer existingRenderer = FindFirstObjectByType<ParticleSystemRenderer>();
        if (existingRenderer != null && existingRenderer.sharedMaterial != null && 
            existingRenderer.sharedMaterial.shader != null)
        {
            Material mat = new Material(existingRenderer.sharedMaterial);
            mat.name = name;
            mat.color = color;
            Debug.Log($"[AR_Oil] Using existing particle material for {name}");
            return mat;
        }
        
        // THIRD: Create material with robust shader finding
        return CreateOilParticleMaterial();
    }
    
    /// <summary>
    /// Create a material for oil particles that won't show as pink
    /// </summary>
    private Material CreateOilParticleMaterial()
    {
        // Use the default particle material and modify it
        // This is the most reliable approach for URP
        
        Material mat = null;
        
        // Try multiple shader paths in order of preference
        string[] shaderNames = new string[]
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Particles/Lit",
            "Particles/Standard Unlit",
            "Particles/Standard Surface",
            "Legacy Shaders/Particles/Alpha Blended",
            "Mobile/Particles/Alpha Blended",
            "Sprites/Default",
            "UI/Default"
        };
        
        Shader foundShader = null;
        foreach (string shaderName in shaderNames)
        {
            foundShader = Shader.Find(shaderName);
            if (foundShader != null)
            {
                Debug.Log($"[AR_Oil] Found shader: {shaderName}");
                break;
            }
        }
        
        if (foundShader != null)
        {
            mat = new Material(foundShader);
        }
        else
        {
            // Ultimate fallback - use default sprite material which always exists
            Debug.LogWarning("[AR_Oil] No particle shader found, using sprite default");
            mat = new Material(Shader.Find("Sprites/Default"));
            if (mat.shader == null)
            {
                // Create a very basic material
                mat = new Material(Shader.Find("UI/Default"));
            }
        }
        
        mat.name = "OilParticleMaterial";
        
        // Set oil color - dark amber/golden brown
        Color oilColor = new Color(0.2f, 0.13f, 0.04f, 1f);
        
        // Try ALL possible color property names
        string[] colorProps = { "_BaseColor", "_Color", "_TintColor", "_MainColor", "_Tint" };
        foreach (string prop in colorProps)
        {
            if (mat.HasProperty(prop))
            {
                mat.SetColor(prop, oilColor);
                Debug.Log($"[AR_Oil] Set color on property: {prop}");
            }
        }
        
        // Also set main texture color if no property found
        mat.color = oilColor;
        
        // Enable transparency
        try
        {
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0);
            if (mat.HasProperty("_Mode"))
                mat.SetFloat("_Mode", 2); // Fade mode
                
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[AR_Oil] Error setting material properties: {e.Message}");
        }
        
        return mat;
    }
    
    
    /// <summary>
    /// Create material for smoke particles (fallback)
    /// </summary>
    private Material CreateSmokeParticleMaterial()
    {
        // Use same robust shader finding as oil material
        string[] shaderNames = new string[]
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Universal Render Pipeline/Particles/Lit",
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Mobile/Particles/Alpha Blended",
            "Sprites/Default",
            "UI/Default"
        };
        
        Shader foundShader = null;
        foreach (string shaderName in shaderNames)
        {
            foundShader = Shader.Find(shaderName);
            if (foundShader != null) break;
        }
        
        Material mat;
        if (foundShader != null)
        {
            mat = new Material(foundShader);
        }
        else
        {
            mat = new Material(Shader.Find("Sprites/Default"));
        }
        
        mat.name = "SmokeParticleMaterial";
        
        Color smokeColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        
        string[] colorProps = { "_BaseColor", "_Color", "_TintColor", "_MainColor" };
        foreach (string prop in colorProps)
        {
            if (mat.HasProperty(prop))
                mat.SetColor(prop, smokeColor);
        }
        mat.color = smokeColor;
        
        try
        {
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
        }
        catch (System.Exception) { }
        
        return mat;
    }
    
    #endregion

    #region === STATE MACHINE ===
    
    private void TransitionToState(InteractionState newState)
    {
        Debug.Log($"[AR_Oil] State: {currentState} → {newState}");
        
        InteractionState previousState = currentState;
        currentState = newState;
        
        // Exit previous state
        OnExitState(previousState);
        
        // Enter new state
        OnEnterState(newState);
    }
    
    private void OnExitState(InteractionState state)
    {
        switch (state)
        {
            case InteractionState.Pouring:
                // Stop pouring effects
                StopActivePourParticles();
                SetEngineVibrationForPouring(false);
                break;

            case InteractionState.PourComplete:
                ResetEngineManipulationState();
                break;
        }
    }
    
    private void OnEnterState(InteractionState state)
    {
        // Stop any previous sequential instructions
        if (sequentialInstructionCoroutine != null)
        {
            StopCoroutine(sequentialInstructionCoroutine);
            sequentialInstructionCoroutine = null;
        }

        // Hide result popup by default on state transitions, to prevent stuck messages
        if (state != InteractionState.PourComplete)
        {
            HideResult();
        }
        
        switch (state)
        {
            case InteractionState.WaitingForPlane:
                UpdateStatus("◉  Scanning");
                sequentialInstructionCoroutine = StartCoroutine(ShowSequentialInstructions(
                    "Scan a flat surface",
                    "Point your camera at the floor"
                ));
                SetBottomActivityVisible(false);
                break;
                
            case InteractionState.Idle:
                UpdateStatus("●  Ready");
                sequentialInstructionCoroutine = StartCoroutine(ShowSequentialInstructions(
                    "Select an oil",
                    "Tap a bottle to begin"
                ));
                ShowPourSlider(false);
                SetBottomActivityVisible(false);
                break;
                
            case InteractionState.Holding:
                string bottleName = selectedOil == OilType.BPCL ? "MAK Premium" : "Local Oil";
                UpdateStatus($"◆  {bottleName}");
                // Use a single, non-looping instruction here
                if (sequentialInstructionCoroutine != null) StopCoroutine(sequentialInstructionCoroutine);
                sequentialInstructionCoroutine = StartCoroutine(ShowSingleInstruction("Prepare to pour"));
                ShowPourSlider(true);
                SetBottomActivityVisible(false);
                break;
                
            case InteractionState.Pouring:
                UpdateStatus("▼  Pouring");
                UpdateInstructions(GetGuidedInstruction(state));
                SetBottomActivityState("POURING", "Aligning bottle to oil inlet", "<color=#86EFAC>◎</color>");
                
                // Crossfade audio smoothly at start of pouring
                AudioClip targetEngineClip = selectedOil == OilType.BPCL ? bpclOilEngineLoop : normalOilEngineLoop;
                if (targetEngineClip != null)
                {
                    CrossfadeEngineLoopClip(targetEngineClip, audioTransitionDuration); 
                }

                if (oilVFXController != null)
                {
                    GameObject bottle = GetBottle(selectedOil);
                    if (bottle != null)
                    {
                        Transform nozzle = bottle.transform.Find("NozzlePoint");
                        oilVFXController.OnPourStart(selectedOil == OilType.BPCL ? 0 : 1, nozzle != null ? nozzle : bottle.transform);
                    }
                }
                if (oilPourManager != null) oilPourManager.OnPourStart(selectedOil == OilType.BPCL ? 0 : 1, GetBottle(selectedOil));
                break;
                
            case InteractionState.PourComplete:
                UpdateInstructions(GetGuidedInstruction(state));
                SetBottomActivityVisible(false);
                if (oilVFXController != null) oilVFXController.OnPourComplete();
                if (oilPourManager != null) oilPourManager.OnPourComplete();
                break;
                
            case InteractionState.PourFailed:
                SetBottomActivityVisible(false);
                StartCoroutine(ReturnToIdleAfterDelay(2f));
                
                // Reset engine audio smoothly if pour failed
                CrossfadeEngineLoopClip(defaultEngineLoop, 1.5f);

                if (oilVFXController != null) oilVFXController.OnPourFailed();
                if (oilPourManager != null) oilPourManager.OnPourFailed();
                break;
        }
    }
    
    private IEnumerator ReturnToIdleAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (currentState == InteractionState.PourFailed)
        {
            TransitionToState(InteractionState.Idle);
        }
    }

    private IEnumerator ShowSequentialInstructions(params string[] instructions)
    {
        if (resultPanel == null || resultText == null) yield break;

        // Wait a frame to ensure UI layout is updated
        yield return null; 

        resultPanel.SetActive(false);

        foreach (var instruction in instructions)
        {
            resultText.text = instruction;
            resultPanel.SetActive(true);
            yield return new WaitForSeconds(2.5f); // Display for 2.5 seconds
            resultPanel.SetActive(false);
            yield return new WaitForSeconds(0.5f); // Pause between instructions
        }
    }

    private IEnumerator ShowSingleInstruction(string instruction)
    {
        if (resultPanel == null || resultText == null) yield break;
        yield return null; // Wait a frame

        resultText.text = instruction;
        resultPanel.SetActive(true);
        yield return new WaitForSeconds(2.5f);
        resultPanel.SetActive(false);
    }
    
    #endregion

    #region === PLANE DETECTION & SPAWNING ===
    
    private void UpdateWaitingForPlane()
    {
        if (hasSpawned) return;
        
        // Debug: Log plane detection status
        int planeCount = 0;
        int horizontalPlanes = 0;
        
        // Check for detected planes
        foreach (var plane in arPlaneManager.trackables)
        {
            planeCount++;
            if (plane.alignment == PlaneAlignment.HorizontalUp)
            {
                horizontalPlanes++;
                Debug.Log($"[AR_Oil] Horizontal plane detected! Total planes: {planeCount}, Horizontal: {horizontalPlanes}. Spawning objects...");
                SpawnObjects();
                return;
            }
        }
        
        // Debug: Log detection progress every 2 seconds
        if (Time.frameCount % 120 == 0)
        {
            Debug.Log($"[AR_Oil] Waiting for plane... Planes detected: {planeCount}, Horizontal: {horizontalPlanes}");
        }
    }
    
    /// <summary>
    /// Spawns engine and bottles in AR world space.
    /// Bottles are positioned to the LEFT and RIGHT of engine, never overlapping.
    /// </summary>
    private void SpawnObjects()
    {
        if (hasSpawned) return;
        hasSpawned = true;
        
        Debug.Log("[AR_Oil] === SPAWNING AR OBJECTS ===");
        
        // === STEP 1: Calculate base spawn position ===
        Vector3 cameraPos = arCamera.transform.position;
        Vector3 cameraForward = arCamera.transform.forward;
        cameraForward.y = 0;
        cameraForward.Normalize();
        
        Vector3 spawnCenter = cameraPos + cameraForward * spawnDistance;
        spawnCenter.y = cameraPos.y - 0.5f; // Slightly below camera height
        
        // Get directional vectors (horizontal only)
        Vector3 rightDir = arCamera.transform.right;
        rightDir.y = 0;
        rightDir.Normalize();
        
        Vector3 forwardDir = cameraForward; // Already normalized and horizontal
        
        // === STEP 2: Spawn Engine at center ===
        Vector3 enginePos = spawnCenter + Vector3.up * engineHeightOffset;
        
        if (enginePrefab != null)
        {
            // Calculate rotation to face camera + apply offset
            Vector3 lookDir = cameraPos - enginePos;
            lookDir.y = 0;
            Quaternion engineRotation = Quaternion.Euler(engineRotationOffset);
            if (lookDir.sqrMagnitude > 0.001f)
            {
                engineRotation = Quaternion.LookRotation(lookDir.normalized) * Quaternion.Euler(engineRotationOffset);
            }
            
            spawnedEngine = Instantiate(enginePrefab, enginePos, engineRotation);
            spawnedEngine.name = "SpawnedEngine";
            spawnedEngine.transform.localScale = Vector3.one * engineScale;
            spawnedEngine.transform.SetParent(null);
            spawnedEngineOriginalPosition = spawnedEngine.transform.position;
            spawnedEngineOriginalRotation = spawnedEngine.transform.rotation;
            spawnedEngineOriginalScale = spawnedEngine.transform.localScale;
            hasCachedEngineTransform = true;
            SetLayerRecursively(spawnedEngine, LayerMask.NameToLayer("Default"));
            EnsureColliders(spawnedEngine);
            
            FindOilTankHole();
            ResolvePersistentEngineRoot();
            FindEngineSmokeParticles();
            StopEngineSmokeParticles();
            
            Debug.Log($"[AR_Oil] ✓ Engine spawned at {enginePos}");
        }
        
        // === STEP 3: Calculate engine bounds for proper bottle placement ===
        float engineHalfWidth = CalculateEngineHalfWidth();
        // Changed to use bottleSpacing as the EXACT distance from the center, ignoring the automatic engine width calculation
        float safeBottleDistance = bottleSpacing;
        
        Debug.Log($"[AR_Oil] Engine half-width: {engineHalfWidth:F3}m, Safe bottle distance: {safeBottleDistance:F3}m");
        
        // === STEP 4: Spawn BPCL Bottle (LEFT of engine) ===
        if (bpclBottlePrefab != null)
        {
            // Position: engine center - right direction * safe distance + forward offset
            Vector3 bpclPos = enginePos 
                - rightDir * safeBottleDistance 
                + forwardDir * bottleForwardOffset
                + Vector3.up * (bottleHeightOffset - engineHeightOffset); // Relative to ground
            
            spawnedBPCLBottle = Instantiate(bpclBottlePrefab, bpclPos, Quaternion.identity);
            spawnedBPCLBottle.name = "BPCL_Bottle";
            spawnedBPCLBottle.transform.localScale = Vector3.one * bottleScale;
            spawnedBPCLBottle.transform.SetParent(null);
            SetLayerRecursively(spawnedBPCLBottle, LayerMask.NameToLayer("Default"));
            EnsureColliders(spawnedBPCLBottle);
            CacheBottlePourParticles(OilType.BPCL, spawnedBPCLBottle);
            
            // Make bottle face camera
            FaceCamera(spawnedBPCLBottle);
            
            float distFromEngine = Vector3.Distance(
                new Vector3(bpclPos.x, 0, bpclPos.z), 
                new Vector3(enginePos.x, 0, enginePos.z)
            );
            Debug.Log($"[AR_Oil] ✓ BPCL Bottle spawned at {bpclPos}, distance from engine: {distFromEngine:F3}m");
        }
        else
        {
            Debug.LogError("[AR_Oil] ✗ BPCL Bottle Prefab is NULL!");
        }
        
        // === STEP 5: Spawn Local Bottle (RIGHT of engine) ===
        if (localBottlePrefab != null)
        {
            // Position: engine center + right direction * safe distance + forward offset
            Vector3 localPos = enginePos 
                + rightDir * safeBottleDistance 
                + forwardDir * bottleForwardOffset
                + Vector3.up * (bottleHeightOffset - engineHeightOffset); // Relative to ground
            
            spawnedLocalBottle = Instantiate(localBottlePrefab, localPos, Quaternion.identity);
            spawnedLocalBottle.name = "Local_Bottle";
            spawnedLocalBottle.transform.localScale = Vector3.one * bottleScale;
            spawnedLocalBottle.transform.SetParent(null);
            SetLayerRecursively(spawnedLocalBottle, LayerMask.NameToLayer("Default"));
            EnsureColliders(spawnedLocalBottle);
            CacheBottlePourParticles(OilType.Local, spawnedLocalBottle);
            
            // Make bottle face camera
            FaceCamera(spawnedLocalBottle);
            
            float distFromEngine = Vector3.Distance(
                new Vector3(localPos.x, 0, localPos.z), 
                new Vector3(enginePos.x, 0, enginePos.z)
            );
            Debug.Log($"[AR_Oil] ✓ Local Bottle spawned at {localPos}, distance from engine: {distFromEngine:F3}m");
        }
        else
        {
            Debug.LogError("[AR_Oil] ✗ Local Bottle Prefab is NULL!");
        }
        
        // === STEP 6: Disable plane detection ===
        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = false;
            foreach (var plane in arPlaneManager.trackables)
            {
                plane.gameObject.SetActive(false);
            }
        }
        
        Debug.Log("[AR_Oil] === SPAWNING COMPLETE ===");
        SetEngineLoopClip(defaultEngineLoop);
        StartCarModelFadeIfConfigured();
        TransitionToState(InteractionState.Idle);
    }
    
    /// <summary>
    /// Calculates the half-width of the engine based on its renderers.
    /// Returns a minimum safe value if calculation fails.
    /// </summary>
    private float CalculateEngineHalfWidth()
    {
        const float MIN_SAFE_DISTANCE = 0.25f; // Minimum 25cm
        const float DEFAULT_HALF_WIDTH = 0.3f; // Default 30cm if no engine
        
        if (spawnedEngine == null) 
            return DEFAULT_HALF_WIDTH;

        GameObject measurementTarget = spawnedPersistentEngineRoot != null
            ? spawnedPersistentEngineRoot.gameObject
            : spawnedEngine;
        
        // Get all renderers in engine
        Renderer[] renderers = measurementTarget.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return DEFAULT_HALF_WIDTH;
        
        // Calculate combined bounds
        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }
        
        // Get the maximum horizontal extent (X or Z axis)
        float halfWidth = Mathf.Max(combinedBounds.extents.x, combinedBounds.extents.z);
        
        // Ensure minimum safe distance
        halfWidth = Mathf.Max(halfWidth, MIN_SAFE_DISTANCE);
        
        Debug.Log($"[AR_Oil] Engine bounds: {combinedBounds.size}, calculated half-width: {halfWidth:F3}m");
        
        return halfWidth;
    }
    
    /// <summary>
    /// Rotates a GameObject to face the AR camera (Y-axis rotation only)
    /// </summary>
    private void FaceCamera(GameObject obj)
    {
        FaceCamera(obj, Vector3.zero);
    }

    private void FaceCamera(GameObject obj, Vector3 rotationOffsetEuler)
    {
        if (obj == null || arCamera == null) return;
        
        Vector3 lookDir = arCamera.transform.position - obj.transform.position;
        lookDir.y = 0; // Keep upright
        
        if (lookDir.sqrMagnitude > 0.001f)
        {
            obj.transform.rotation = Quaternion.LookRotation(lookDir.normalized) * Quaternion.Euler(rotationOffsetEuler);
        }
    }

    private void UpdateCameraFacingAlignment()
    {
        if (!keepObjectsFacingCamera || arCamera == null)
            return;

        bool allowEngineManipulation = enablePostPourEngineManipulation && currentState == InteractionState.PourComplete;

        if (keepPanelFacingCamera && arBenefitsPanel != null)
        {
            FaceCamera(arBenefitsPanel, panelFacingRotationOffset);
        }

        // Avoid overriding bottle movement/tilt while pouring or while return animation is active.
        if (currentState == InteractionState.Pouring || isBottleReturnAnimating)
        {
            if (keepEngineFacingCamera && spawnedEngine != null && !allowEngineManipulation)
            {
                FaceCamera(spawnedEngine, engineRotationOffset);
            }
            return;
        }

        if (keepEngineFacingCamera && spawnedEngine != null && !allowEngineManipulation)
        {
            FaceCamera(spawnedEngine, engineRotationOffset);
        }

        if (spawnedBPCLBottle != null)
        {
            FaceCamera(spawnedBPCLBottle);
        }

        if (spawnedLocalBottle != null)
        {
            FaceCamera(spawnedLocalBottle);
        }
    }
    
    private void FindOilTankHole()
    {
        if (spawnedEngine == null) return;
        
        string[] possibleNames = { "OilTankHole", "OilHole", "OilCap", "OilFill", "FillHole", "oiltank" };
        
        foreach (string name in possibleNames)
        {
            Transform found = spawnedEngine.transform.Find(name);
            if (found == null)
            {
                // Search recursively
                foreach (Transform child in spawnedEngine.GetComponentsInChildren<Transform>())
                {
                    if (child.name.ToLower().Contains(name.ToLower()))
                    {
                        oilTankHole = child;
                        Debug.Log($"[AR_Oil] Found oil tank hole: {child.name}");
                        return;
                    }
                }
            }
            else
            {
                oilTankHole = found;
                Debug.Log($"[AR_Oil] Found oil tank hole: {name}");
                return;
            }
        }
        
        Debug.Log("[AR_Oil] Oil tank hole not found, using offset");
    }

    private void ResolvePersistentEngineRoot()
    {
        if (spawnedEngine == null)
        {
            spawnedPersistentEngineRoot = null;
            return;
        }

        Transform spawnRoot = spawnedEngine.transform;

        if (oilTankHole != null)
        {
            Transform current = oilTankHole;
            while (current != null && current.parent != null && current.parent != spawnRoot)
            {
                current = current.parent;
            }

            if (current != null && current.parent == spawnRoot)
            {
                spawnedPersistentEngineRoot = current;
                Debug.Log($"[AR_Oil] Persistent engine root resolved from oil hole: {spawnedPersistentEngineRoot.name}");
                return;
            }
        }

        foreach (Transform child in spawnRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child == spawnRoot)
                continue;

            if (child.name.IndexOf("engine", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Transform topLevelChild = GetTopLevelChildUnderRoot(child, spawnRoot);
            if (topLevelChild != null)
            {
                spawnedPersistentEngineRoot = topLevelChild;
                Debug.Log($"[AR_Oil] Persistent engine root resolved by name: {spawnedPersistentEngineRoot.name}");
                return;
            }
        }

        spawnedPersistentEngineRoot = spawnRoot;
        Debug.Log("[AR_Oil] Persistent engine root fallback to spawn root");
    }

    private Transform GetTopLevelChildUnderRoot(Transform node, Transform root)
    {
        if (node == null || root == null)
            return null;

        Transform current = node;
        while (current != null && current.parent != null && current.parent != root)
        {
            current = current.parent;
        }

        if (current != null && current.parent == root)
            return current;

        return null;
    }
    
    /// <summary>
    /// Find smoke particle systems in the engine prefab
    /// </summary>
    private void FindEngineSmokeParticles()
    {
        if (spawnedEngine == null) return;

        engineSmokeParticles.Clear();
        engineSmokeDefaultStartColors.Clear();

        Transform primarySearchRoot = spawnedPersistentEngineRoot != null
            ? spawnedPersistentEngineRoot
            : spawnedEngine.transform;

        bool foundByName = CollectEngineSmokeParticlesByName(primarySearchRoot, false);

        if (!foundByName && primarySearchRoot != spawnedEngine.transform)
        {
            foundByName = CollectEngineSmokeParticlesByName(spawnedEngine.transform, false);
        }

        if (!foundByName)
        {
            bool addedFallback = CollectEngineSmokeParticlesByName(primarySearchRoot, true);
            if (!addedFallback && primarySearchRoot != spawnedEngine.transform)
            {
                CollectEngineSmokeParticlesByName(spawnedEngine.transform, true);
            }
        }
        
        Debug.Log($"[AR_Oil] Found {engineSmokeParticles.Count} smoke particles in engine prefab");
    }

    private bool CollectEngineSmokeParticlesByName(Transform root, bool allowAllParticles)
    {
        if (root == null)
            return false;

        bool found = false;
        ParticleSystem[] allParticles = root.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in allParticles)
        {
            if (ps == null || engineSmokeParticles.Contains(ps))
                continue;

            // DO NOT collect the OilBubbleParticles as Engine Smoke
            if (ps.CompareTag("OilBubble") || ps.gameObject.name.Contains("OilBubble"))
                continue;

            string psName = ps.gameObject.name.ToLower();
            bool isSmokeNamed = psName.Contains("smoke") || psName.Contains("exhaust") ||
                                psName.Contains("fume") || psName.Contains("emission") ||
                                psName.Contains("particle");

            if (!allowAllParticles && !isSmokeNamed)
                continue;

            engineSmokeParticles.Add(ps);
            if (!engineSmokeDefaultStartColors.ContainsKey(ps))
            {
                engineSmokeDefaultStartColors[ps] = ps.main.startColor;
            }
            SetEngineSmokeParticleActive(ps, false);
            found = true;

            if (allowAllParticles)
            {
                Debug.Log($"[AR_Oil] Added engine particle (fallback): {ps.gameObject.name}");
            }
            else
            {
                Debug.Log($"[AR_Oil] Found engine smoke particle: {ps.gameObject.name}");
            }
        }

        return found;
    }
    
    /// <summary>
    /// Play all engine smoke particles
    /// </summary>
    private void PlayEngineSmokeParticles(Color? customColor = null, float? customAlpha = null)
    {
        foreach (ParticleSystem ps in engineSmokeParticles)
        {
            if (ps == null)
                continue;

            SetEngineSmokeParticleActive(ps, true);

            if (customColor.HasValue || customAlpha.HasValue)
            {
                ApplyEngineSmokeTint(ps, customColor, customAlpha);
            }

            if (!ps.isPlaying)
            {
                ps.Play();
            }
        }
        
        if (customColor.HasValue || customAlpha.HasValue)
        {
            Debug.Log($"[AR_Oil] Playing {engineSmokeParticles.Count} engine smoke particles with custom color/alpha");
        }
        else
        {
            Debug.Log($"[AR_Oil] Playing {engineSmokeParticles.Count} engine smoke particles with default appearance");
        }
    }
    
    /// <summary>
    /// Stop all engine smoke particles
    /// </summary>
    private void StopEngineSmokeParticles()
    {
        foreach (ParticleSystem ps in engineSmokeParticles)
        {
            SetEngineSmokeParticleActive(ps, false);
        }
        
        Debug.Log("[AR_Oil] Stopped engine smoke particles");
    }

    private void SetEngineSmokeParticleActive(ParticleSystem particle, bool isActive)
    {
        if (particle == null)
            return;

        ParticleSystem.MainModule main = particle.main;
        main.playOnAwake = isActive;

        ParticleSystem.EmissionModule emission = particle.emission;
        emission.enabled = isActive;

        if (isActive)
        {
            if (!particle.isPlaying)
            {
                particle.Play(true);
            }
            return;
        }

        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particle.Clear(true);
    }

    private void ApplyEngineSmokeTint(ParticleSystem particle, Color? customColor, float? customAlpha)
    {
        if (particle == null)
            return;

        ParticleSystem.ColorOverLifetimeModule colorModule = particle.colorOverLifetime;
        Gradient gradient = colorModule.color.gradient;

        Color startColor = gradient.Evaluate(0f);
        
        if (customColor.HasValue)
        {
            startColor = customColor.Value;
        }
        
        if (customAlpha.HasValue)
        {
            startColor.a = Mathf.Clamp01(customAlpha.Value);
        }

        ParticleSystem.MainModule main = particle.main;
        main.startColor = startColor;

        Debug.Log($"[AR_Oil] Applied smoke tint: {startColor.ToString()} to {particle.gameObject.name}");
    }

    private void RestoreEngineSmokeDefaultStartColors()
    {
        foreach (ParticleSystem ps in engineSmokeParticles)
        {
            if (ps == null)
                continue;

            if (!engineSmokeDefaultStartColors.TryGetValue(ps, out ParticleSystem.MinMaxGradient defaultStartColor))
                continue;

            ParticleSystem.MainModule main = ps.main;
            main.startColor = defaultStartColor;
        }
    }
    
    private Vector3 GetOilTankPosition()
    {
        if (oilTankHole != null)
            return oilTankHole.position;
        
        if (spawnedEngine != null)
            return spawnedEngine.transform.position + oilTankOffset;
        
        return Vector3.zero;
    }
    
    #endregion

    #region === TOUCH INPUT ===
    
    private void HandleTouchInput()
    {
        Vector2? touchPos = GetTouchPosition();
        if (!touchPos.HasValue) return;
        
        ProcessTouch(touchPos.Value);
    }

    private void HandlePostPourInput()
    {
        if (!enablePostPourEngineManipulation)
        {
            HandleTouchInput();
            return;
        }

#if ENABLE_INPUT_SYSTEM
        if (HandlePostPourInputEnhanced())
        {
            return;
        }
#else
        if (HandlePostPourInputLegacy())
        {
            return;
        }
#endif

        HandleTouchInput();
    }
    
    private Vector2? GetTouchPosition()
    {
        #if ENABLE_INPUT_SYSTEM
        if (Touch.activeTouches.Count > 0)
        {
            var touch = Touch.activeTouches[0];
            if (touch.phase == TouchPhase.Began)
            {
                return touch.screenPosition;
            }
        }
        return null;
        #else
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase == UnityEngine.TouchPhase.Began)
            {
                return touch.position;
            }
        }
        return null;
        #endif
    }
    
    private void ProcessTouch(Vector2 screenPosition)
    {
        if (TryGetTappedBottle(screenPosition, out OilType tappedOil))
        {
            HandleBottleTap(tappedOil);
        }
    }

    private bool TryGetTappedBottle(Vector2 screenPosition, out OilType tappedOil)
    {
        tappedOil = OilType.None;

        Ray ray = arCamera.ScreenPointToRay(screenPosition);
        
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            tappedOil = IdentifyTappedBottle(hit.collider.gameObject);
            return tappedOil != OilType.None;
        }

        return false;
    }

    private bool HandlePostPourInputEnhanced()
    {
        if (Touch.activeTouches.Count >= 2)
        {
            EndEngineRotateGesture();

            var firstTouch = Touch.activeTouches[0];
            var secondTouch = Touch.activeTouches[1];

            HandleEnginePinch(firstTouch.screenPosition, secondTouch.screenPosition);
            return true;
        }

        if (isEnginePinchActive)
        {
            isEnginePinchActive = false;
            enginePinchStartDistance = 0f;
            enginePinchStartScale = Vector3.one;
        }

        if (Touch.activeTouches.Count == 1)
        {
            var touch = Touch.activeTouches[0];

            if (touch.phase == TouchPhase.Began)
            {
                if (TryGetTappedBottle(touch.screenPosition, out OilType tappedOil))
                {
                    HandleBottleTap(tappedOil);
                    return true;
                }

                if (IsTouchOverUI(touch.touchId))
                {
                    return true;
                }

                BeginEngineRotateGesture(touch.touchId, touch.screenPosition);
                return true;
            }

            if (isEngineRotateGestureActive && touch.touchId == engineRotateTouchId)
            {
                if (touch.phase == TouchPhase.Moved)
                {
                    UpdateEngineRotateGesture(touch.screenPosition);
                    return true;
                }

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    EndEngineRotateGesture();
                    return true;
                }
            }
        }
        else if (Touch.activeTouches.Count == 0)
        {
            ResetEngineManipulationState();
        }

        return isEngineRotateGestureActive || isEnginePinchActive;
    }

    private bool HandlePostPourInputLegacy()
    {
        if (Input.touchCount >= 2)
        {
            EndEngineRotateGesture();

            UnityEngine.Touch firstTouch = Input.GetTouch(0);
            UnityEngine.Touch secondTouch = Input.GetTouch(1);

            HandleEnginePinch(firstTouch.position, secondTouch.position);
            return true;
        }

        if (isEnginePinchActive)
        {
            isEnginePinchActive = false;
            enginePinchStartDistance = 0f;
            enginePinchStartScale = Vector3.one;
        }

        if (Input.touchCount == 1)
        {
            UnityEngine.Touch touch = Input.GetTouch(0);

            if (touch.phase == UnityEngine.TouchPhase.Began)
            {
                if (TryGetTappedBottle(touch.position, out OilType tappedOil))
                {
                    HandleBottleTap(tappedOil);
                    return true;
                }

                if (IsTouchOverUI(touch.fingerId))
                {
                    return true;
                }

                BeginEngineRotateGesture(touch.fingerId, touch.position);
                return true;
            }

            if (isEngineRotateGestureActive && touch.fingerId == engineRotateTouchId)
            {
                if (touch.phase == UnityEngine.TouchPhase.Moved)
                {
                    UpdateEngineRotateGesture(touch.position);
                    return true;
                }

                if (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled)
                {
                    EndEngineRotateGesture();
                    return true;
                }
            }
        }
        else if (Input.touchCount == 0)
        {
            ResetEngineManipulationState();
        }

        return isEngineRotateGestureActive || isEnginePinchActive;
    }

    private bool IsTouchOverUI(int pointerId)
    {
        if (EventSystem.current == null)
            return false;

        return EventSystem.current.IsPointerOverGameObject(pointerId);
    }

    private void BeginEngineRotateGesture(int touchId, Vector2 touchPosition)
    {
        engineRotateTouchId = touchId;
        engineRotateLastPosition = touchPosition;
        isEngineRotateGestureActive = true;
        isEnginePinchActive = false;
    }

    private void UpdateEngineRotateGesture(Vector2 currentPosition)
    {
        Transform engineTransform = GetManipulableEngineTransform();
        if (engineTransform == null)
            return;

        Vector2 delta = currentPosition - engineRotateLastPosition;
        engineRotateLastPosition = currentPosition;

        if (delta.sqrMagnitude < 0.0001f)
            return;

        if (allowEngineRotationYAxis && Mathf.Abs(delta.x) > 0.01f)
        {
            float yawAmount = delta.x * engineRotationSensitivityY;
            engineTransform.Rotate(0f, -yawAmount, 0f, Space.Self);
        }

        if (allowEngineRotationXAxis && Mathf.Abs(delta.y) > 0.01f)
        {
            float pitchAmount = delta.y * engineRotationSensitivityX;
            engineTransform.Rotate(pitchAmount, 0f, 0f, Space.Self);
        }
    }

    private void EndEngineRotateGesture()
    {
        isEngineRotateGestureActive = false;
        engineRotateTouchId = -1;
    }

    private void HandleEnginePinch(Vector2 firstTouchPosition, Vector2 secondTouchPosition)
    {
        Transform engineTransform = GetManipulableEngineTransform();
        if (engineTransform == null)
            return;

        float currentDistance = Vector2.Distance(firstTouchPosition, secondTouchPosition);
        if (!isEnginePinchActive)
        {
            isEnginePinchActive = true;
            enginePinchStartDistance = currentDistance;
            enginePinchStartScale = engineTransform.localScale;
            return;
        }

        if (enginePinchStartDistance <= 0.001f)
            return;

        float distanceDelta = currentDistance - enginePinchStartDistance;
        float currentUniformScale = engineTransform.localScale.x;
        float targetUniformScale = currentUniformScale + distanceDelta * enginePinchZoomSensitivity;

        float baseScale = hasCachedEngineTransform ? spawnedEngineOriginalScale.x : Mathf.Max(0.01f, engineTransform.localScale.x);
        float minScale = Mathf.Max(baseScale * 0.8f, baseScale * Mathf.Max(0.1f, minEngineManipulationScale));
        float maxScale = Mathf.Max(minScale + 0.01f, baseScale * Mathf.Max(minEngineManipulationScale + 0.1f, maxEngineManipulationScale));

        float uniformScale = Mathf.Clamp(targetUniformScale, minScale, maxScale);
        engineTransform.localScale = Vector3.one * uniformScale;

        enginePinchStartDistance = currentDistance;
        enginePinchStartScale = engineTransform.localScale;
    }

    private void ResetEngineManipulationState()
    {
        EndEngineRotateGesture();
        isEnginePinchActive = false;
        enginePinchStartDistance = 0f;
        enginePinchStartScale = Vector3.one;
    }

    private Transform GetManipulableEngineTransform()
    {
        if (spawnedEngine == null)
            return null;

        return spawnedEngine.transform;
    }
    
    private OilType IdentifyTappedBottle(GameObject hitObject)
    {
        // Check BPCL bottle
        if (IsChildOf(hitObject, spawnedBPCLBottle))
            return OilType.BPCL;
        
        // Check Local bottle
        if (IsChildOf(hitObject, spawnedLocalBottle))
            return OilType.Local;
        
        return OilType.None;
    }
    
    private bool IsChildOf(GameObject child, GameObject parent)
    {
        if (parent == null) return false;
        
        Transform current = child.transform;
        while (current != null)
        {
            if (current.gameObject == parent)
                return true;
            current = current.parent;
        }
        return false;
    }
    
    private void HandleBottleTap(OilType tappedOil)
    {
        Debug.Log($"[AR_Oil] Bottle tapped: {tappedOil}, Current state: {currentState}, Selected: {selectedOil}");
        
        switch (currentState)
        {
            case InteractionState.Idle:
                SelectBottle(tappedOil);
                break;
                
            case InteractionState.Holding:
                if (tappedOil == selectedOil)
                {
                    // Tap same bottle = deselect
                    DeselectBottle();
                }
                else
                {
                    // Tap different bottle = switch
                    DeselectBottle();
                    SelectBottle(tappedOil);
                }
                break;
                
            case InteractionState.PourComplete:
                // Always allow another attempt after a completed pour.
                ResetEngineState();
                SelectBottle(tappedOil);
                break;
        }
    }
    
    #endregion

    #region === BOTTLE SELECTION ===
    
    private void SelectBottle(OilType oil)
    {
        if (oil == OilType.None) return;
        
        hasSelectedAnyBottle = true;
        selectedOil = oil;
        GameObject bottle = GetBottle(oil);
        
        if (bottle != null)
        {
            // Store original transform
            selectedBottleOriginalPos = bottle.transform.position;
            selectedBottleOriginalRot = bottle.transform.rotation;
            selectedBottlePourBaseRot = selectedBottleOriginalRot;
            
            // Apply highlight and remove after 1 second
            ApplyHighlight(bottle);
            StartCoroutine(RemoveHighlightAfterDelay(bottle, 1.0f));
            
            // Play sound
            PlaySound(selectSound);
        }
        
        pourProgress = 0f;
        smoothedTiltValue = 0f;
        
        TransitionToState(InteractionState.Holding);
    }
    
    private IEnumerator RemoveHighlightAfterDelay(GameObject bottle, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (bottle != null)
        {
            RemoveHighlight(bottle);
        }
    }
    
    private void DeselectBottle()
    {
        if (selectedOil == OilType.None) return;
        
        Debug.Log($"[AR_Oil] Deselecting: {selectedOil}");
        
        GameObject bottle = GetBottle(selectedOil);
        
        if (bottle != null)
        {
            // Remove highlight
            RemoveHighlight(bottle);
            
            // Return to original position
            StartCoroutine(AnimateBottleReturn(bottle, selectedBottleOriginalPos, selectedBottleOriginalRot));
        }
        
        // Play sound
        PlaySound(deselectSound);
        
        // Reset state
        selectedOil = OilType.None;
        pourProgress = 0f;
        
        TransitionToState(InteractionState.Idle);
    }
    
    private GameObject GetBottle(OilType oil)
    {
        return oil switch
        {
            OilType.BPCL => spawnedBPCLBottle,
            OilType.Local => spawnedLocalBottle,
            _ => null
        };
    }
    
    #endregion

    #region === HOLDING STATE ===
    
    private void UpdateHoldingState()
    {
        float tiltValue = GetTiltValue();
        
        // Smooth the tilt value
        smoothedTiltValue = Mathf.Lerp(smoothedTiltValue, tiltValue, accelSmoothing);
        
        // Check if user is tilting enough to START pouring
        // We use absolute value because we want "forward tilt" which is typically negative Y
        float absTilt = Mathf.Abs(smoothedTiltValue);
        
        if (absTilt >= tiltStartThreshold)
        {
            // Check distance from engine
            float distance = Vector3.Distance(arCamera.transform.position, spawnedEngine.transform.position);
            
            if (distance > maxPourDistance)
            {
                UpdateStatus("Move closer to the engine");
                return;
            }
            
            // Start pouring!
            StartPouring();
        }
        
        // Update pour slider to show tilt progress
        if (pourSlider != null)
        {
            float tiltProgress = Mathf.InverseLerp(0, tiltStartThreshold, absTilt);
            pourSlider.value = tiltProgress * 0.3f; // Show up to 30% before actual pour starts
        }
    }
    
    private float GetTiltValue()
    {
        #if ENABLE_INPUT_SYSTEM
        if (Accelerometer.current != null && Accelerometer.current.enabled)
        {
            // Y axis: positive = tilted back, negative = tilted forward
            // We want absolute tilt amount
            return Accelerometer.current.acceleration.ReadValue().y;
        }
        else if (GravitySensor.current != null && GravitySensor.current.enabled)
        {
            return GravitySensor.current.gravity.ReadValue().y;
        }
        #endif
        
        // Fallback
        return Input.acceleration.y;
    }
    
    #endregion

    #region === POURING STATE ===
    
    private void StartPouring()
    {
        Debug.Log($"[AR_Oil] Starting pour! Tilt={smoothedTiltValue:F3}");

        GameObject bottle = GetBottle(selectedOil);
        if (bottle != null)
        {
            // Lock rotation baseline at pour start to avoid snap when entering tilt phase.
            selectedBottlePourBaseRot = bottle.transform.rotation;
            selectedBottleOriginalRot = selectedBottlePourBaseRot;
        }
        
        pourProgress = 0f;
        pourElapsedTime = 0f;
        BeginPourScoreTracking();

        // Vibrate specifically for normal/local oil while pouring.
        if (selectedOil == OilType.Local)
        {
            SetEngineVibrationForPouring(true);
            nextVibratePulseTime = Time.time;
        }
        else
        {
            SetEngineVibrationForPouring(false);
        }
        
        // NOTE: Particles will start in AnimateBottlePouring when bottle is tilted
        // Don't start them here - they should only play when bottle cap is positioned correctly
        
        // Play pouring sound
        PlaySound(pouringSound);
        
        TransitionToState(InteractionState.Pouring);
    }
    
    private void UpdatePouringState()
    {
        float tiltValue = GetTiltValue();
        smoothedTiltValue = Mathf.Lerp(smoothedTiltValue, tiltValue, accelSmoothing);
        float absTilt = Mathf.Abs(smoothedTiltValue);

        if (selectedOil == OilType.Local && Time.time >= nextVibratePulseTime)
        {
            TriggerEngineAnimation("Vibrate");
            nextVibratePulseTime = Time.time + 0.6f;
        }

        if (enablePourScoring)
        {
            TrackPourScoreSample(absTilt);
        }
        
        // Check distance
        float distance = Vector3.Distance(arCamera.transform.position, spawnedEngine.transform.position);
        if (distance > maxPourDistance)
        {
            FailPour("Move closer to engine");
            return;
        }

        float approach = Mathf.Max(0.05f, bottleApproachDuration);
        float tilt = Mathf.Max(0.05f, bottleTiltDuration);
        float hold = Mathf.Max(0.1f, pourHoldDuration);
        float totalDuration = approach + tilt + hold;

        pourElapsedTime += Time.deltaTime;
        pourProgress = Mathf.Clamp01(pourElapsedTime / totalDuration);
        
        // Update UI
        if (pourSlider != null)
        {
            pourSlider.value = pourProgress;
        }

        string phaseText;
        string phaseIcon;
        if (pourElapsedTime < approach)
        {
            phaseText = "Positioning bottle";
            phaseIcon = "<color=#86EFAC>◎</color>";
        }
        else if (pourElapsedTime < approach + tilt)
        {
            phaseText = "Adjusting pour angle";
            phaseIcon = "<color=#93C5FD>∠</color>";
        }
        else
        {
            phaseText = "Flowing oil to engine";
            phaseIcon = "<color=#67E8F9>~</color>";
        }
        int percent = Mathf.RoundToInt(pourProgress * 100f);
        SetBottomActivityState("POURING", $"{phaseText}  {percent}%", phaseIcon);
        
        // Update bottle animation
        AnimateBottlePouring(pourElapsedTime);
        
        // Check completion
        if (pourElapsedTime >= totalDuration)
        {
            CompletePour();
        }
    }
    
    private void AnimateBottlePouring(float elapsedTime)
    {
        GameObject bottle = GetBottle(selectedOil);
        if (bottle == null) return;
        
        Vector3 targetPos = GetOilTankPosition() + Vector3.up * pourTargetHeightOffset;
        Vector3 oilPos = GetOilTankPosition();

        float approach = Mathf.Max(0.05f, bottleApproachDuration);
        float tilt = Mathf.Max(0.05f, bottleTiltDuration);

        // Phase 1: quick approach to engine
        if (elapsedTime < approach)
        {
            float t = elapsedTime / approach;
            t = EaseOutQuad(t);
            
            // Arc motion upward
            Vector3 midPoint = Vector3.Lerp(selectedBottleOriginalPos, targetPos, 0.5f);
            midPoint.y += 0.2f; // Arc peak
            
            Vector3 pos = QuadraticBezier(selectedBottleOriginalPos, midPoint, targetPos, t);
            bottle.transform.position = pos;

            AlignBottleCapAboveOilHole(bottle, oilPos);
            
            // Particles not playing yet in phase 1
            StopActivePourParticles();
        }
        // Phase 2: rotate into pouring pose
        else if (elapsedTime < approach + tilt)
        {
            float t = (elapsedTime - approach) / tilt;
            t = EaseInOutQuad(t);

            bottle.transform.position = targetPos;

            float tiltAngle = Mathf.Lerp(0f, pourTiltAngle, t);
            Quaternion pourRotation = selectedBottlePourBaseRot * Quaternion.Euler(tiltAngle, 0f, 0f);
            bottle.transform.rotation = pourRotation;

            AlignBottleCapAboveOilHole(bottle, oilPos);

            if (t > 0.2f)
            {
                UpdatePourParticles(bottle);
            }
        }
        // Phase 3: hold pouring for configured seconds
        else
        {
            bottle.transform.position = targetPos;
            Quaternion holdRotation = selectedBottlePourBaseRot * Quaternion.Euler(pourTiltAngle, 0f, 0f);
            bottle.transform.rotation = holdRotation;
            AlignBottleCapAboveOilHole(bottle, oilPos);
            UpdatePourParticles(bottle);
        }
    }

    private void UpdatePourParticles(GameObject bottle)
    {
        if (bottle == null)
            return;

        Vector3 oilPos = GetOilTankPosition();
        Vector3 bottleCapPos = GetBottleCapPosition(bottle);

        if (AlignBottleCapAboveOilHole(bottle, oilPos))
        {
            bottleCapPos = GetBottleCapPosition(bottle);
        }
        else
        {
            // Ensure bottle neck stays above oil hole for a natural pour arc.
            float minCapY = oilPos.y + minNeckHeightAboveOilHole;
            if (bottleCapPos.y < minCapY)
            {
                float raise = minCapY - bottleCapPos.y;
                bottle.transform.position += Vector3.up * raise;
                bottleCapPos += Vector3.up * raise;
            }
        }

        List<ParticleSystem> bottleParticles = GetCurrentBottlePourParticles();
        if (bottleParticles.Count > 0)
        {
            foreach (ParticleSystem ps in bottleParticles)
            {
                if (ps == null)
                    continue;

                if (!ps.gameObject.activeSelf)
                {
                    ps.gameObject.SetActive(true);
                }

                var emissionLocal = ps.emission;
                if (!emissionLocal.enabled)
                {
                    emissionLocal.enabled = true;
                }

                if (!ps.isPlaying)
                {
                    ps.Play(true);
                }
            }

            return;
        }

        if (pouringParticles == null)
            return;

        pouringParticles.transform.position = bottleCapPos;

        Vector3 toTarget = oilPos - bottleCapPos;
        Vector3 pourDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.down;
        pouringParticles.transform.rotation = Quaternion.LookRotation(pourDirection);

        if (!pouringParticles.gameObject.activeSelf)
        {
            pouringParticles.gameObject.SetActive(true);
        }

        var emission = pouringParticles.emission;
        if (!emission.enabled)
        {
            emission.enabled = true;
        }

        if (!pouringParticles.isPlaying)
        {
            pouringParticles.Play(true);
            Debug.Log($"[AR_Oil] Particles started at bottle cap: {bottleCapPos}");
        }
    }

    private void CacheBottlePourParticles(OilType oil, GameObject bottle)
    {
        if (bottle == null || oil == OilType.None)
            return;

        if (!bottlePourParticles.TryGetValue(oil, out List<ParticleSystem> list))
        {
            list = new List<ParticleSystem>();
            bottlePourParticles[oil] = list;
        }
        else
        {
            list.Clear();
        }

        ParticleSystem[] allParticles = bottle.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in allParticles)
        {
            if (ps == null)
                continue;

            list.Add(ps);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        Debug.Log($"[AR_Oil] Cached {list.Count} pour particles for {oil}");
    }

    private List<ParticleSystem> GetCurrentBottlePourParticles()
    {
        if (selectedOil != OilType.None && bottlePourParticles.TryGetValue(selectedOil, out List<ParticleSystem> list) && list != null)
        {
            return list;
        }

        return emptyBottleParticleList;
    }

    private void StopActivePourParticles()
    {
        List<ParticleSystem> bottleParticles = GetCurrentBottlePourParticles();
        for (int i = 0; i < bottleParticles.Count; i++)
        {
            ParticleSystem ps = bottleParticles[i];
            if (ps != null)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        if (pouringParticles != null)
        {
            pouringParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
    
    /// <summary>
    /// Get the position of the bottle cap/tip (where oil pours from)
    /// </summary>
    private Vector3 GetBottleCapPosition(GameObject bottle)
    {
        if (bottle == null) return Vector3.zero;
        
        Transform capTransform = FindBottleCapTransform(bottle);
        if (capTransform != null)
        {
            return capTransform.position;
        }
        
        // Fallback: keep cap estimate above bottle in world up to preserve height clearance.
        Vector3 capOffset = Vector3.up * bottleCapHeight;
        return bottle.transform.position + capOffset;
    }

    private bool AlignBottleCapAboveOilHole(GameObject bottle, Vector3 oilPos)
    {
        if (bottle == null || oilTankHole == null)
            return false;

        Transform capTransform = FindBottleCapTransform(bottle);
        if (capTransform == null)
            return false;

        Vector3 currentCapPos = capTransform.position;
        Vector3 desiredCapPos = new Vector3(oilPos.x, Mathf.Max(currentCapPos.y, oilPos.y + minNeckHeightAboveOilHole), oilPos.z);
        Vector3 delta = desiredCapPos - currentCapPos;

        if (delta.sqrMagnitude <= 0.000001f)
            return true;

        bottle.transform.position += delta;
        return true;
    }

    private Transform FindBottleCapTransform(GameObject bottle)
    {
        if (bottle == null)
            return null;

        string[] capNames = { "Cap", "Tip", "Nozzle", "PourPoint", "Spout", "Top" };
        foreach (string name in capNames)
        {
            Transform cap = bottle.transform.Find(name);
            if (cap != null)
                return cap;

            foreach (Transform child in bottle.GetComponentsInChildren<Transform>())
            {
                if (child.name.ToLower().Contains(name.ToLower()))
                    return child;
            }
        }

        return null;
    }
    
    private void FailPour(string message)
    {
        Debug.LogWarning($"[AR_Oil] Pour failed: {message}");
        
        // Stop particles
        StopActivePourParticles();
        
        // Play warning sound
        PlaySound(warningSound);
        
        // Reset bottle
        GameObject bottle = GetBottle(selectedOil);
        if (bottle != null)
        {
            StartCoroutine(AnimateBottleReturn(bottle, selectedBottleOriginalPos, selectedBottleOriginalRot));
            RemoveHighlight(bottle);
        }
        
        // Show error - minimal style
        ShowResult($"<color=#F87171>✗</color>  {message}", new Color(0.15f, 0.08f, 0.08f, 0.92f));
        
        // Reset state
        selectedOil = OilType.None;
        pourProgress = 0f;
        
        TransitionToState(InteractionState.PourFailed);
    }
    
    private void CompletePour()
    {
        Debug.Log($"[AR_Oil] Pour complete! Oil: {selectedOil}");

        OilType completedOil = selectedOil;
        
        // Stop particles
        StopActivePourParticles();
        
        // Play success sound
        PlaySound(successSound);
        
        // Return bottle
        GameObject bottle = GetBottle(selectedOil);
        if (bottle != null)
        {
            StartCoroutine(AnimateBottleReturn(bottle, selectedBottleOriginalPos, selectedBottleOriginalRot));
            RemoveHighlight(bottle);
        }
        
        // Remember which oil was poured
        lastPouredOil = completedOil;

        if (enablePourScoring)
        {
            RecordCompletedPourScore(completedOil);
        }
        
        // Apply effects
        if (completedOil == OilType.BPCL)
        {
            ApplyBPCLEffect();
        }
        else if (completedOil == OilType.Local)
        {
            ApplyLocalOilEffect();
        }
        
        // Reset selection
        selectedOil = OilType.None;

        TransitionToState(InteractionState.PourComplete);
    }
    
    #endregion

    #region === ENGINE EFFECTS ===
    
    private void ApplyBPCLEffect()
    {
        UpdateStatus("<color=#4ADE80>●</color>  Optimal Performance");

        SetEngineLoopClip(bpclOilEngineLoop);
        
        // Play smoke with white color and very low alpha (faint)
        PlayEngineSmokeParticles(Color.white, 0.05f);
        
        // Trigger smooth animation
        TriggerEngineAnimation("Smooth");
        
        // Show benefits panel
        ShowBPCLBenefits();

        // 🟢 ACTIVATE OIL BUBBLES ON PISTON (Since it's not an animation)
        EngineSimulation.PistonOilParticleController bubblesController = spawnedEngine.GetComponentInChildren<EngineSimulation.PistonOilParticleController>(true);
        if (bubblesController != null)
        {
            bubblesController.ActivateOilBubbles();
        }
        else
        {
            // Fallback: if the Piston script is missing, just find the particle directly by name/tag
            ParticleSystem[] allPS = spawnedEngine.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in allPS)
            {
                if (ps.CompareTag("OilBubble") || ps.gameObject.name.Contains("OilBubble"))
                {
                    ps.gameObject.SetActive(true);
                    ps.Play(true);
                    Debug.Log($"[AR_Oil] Activated OilBubble particle '{ps.name}' directly via fallback.");
                }
            }
        }

        // FADE ENGINE OUTER BODY TO REVEAL INSIDE
        StartCoroutine(FadeEngineOuterBodies(engineBodyTransparentAlpha, engineBodyFadeDuration));

        // APPLY GOLDEN GLOW TO TARGET MATERIAL SMOOTHLY
        StartCoroutine(FadeEngineGlowEffect(bpclGlowColor, true, engineGlowTransitionDuration));
    }
    
    private IEnumerator FadeEngineGlowEffect(Color targetColor, bool enableEmission, float duration)
    {
        if (spawnedEngine == null || engineGlowMaterial == null) yield break;

        Renderer[] allRenderers = spawnedEngine.GetComponentsInChildren<Renderer>(true);
        List<(Material mat, Color startColor, Color startEmis)> matsToFade = new List<(Material, Color, Color)>();

        foreach (Renderer r in allRenderers)
        {
            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                Material sharedMat = r.sharedMaterials[i];
                if (sharedMat != null && (sharedMat == engineGlowMaterial || sharedMat.name.StartsWith(engineGlowMaterial.name)))
                {
                    Material instanceMat = r.materials[i];
                    Color sCol = instanceMat.HasProperty("_BaseColor") ? instanceMat.GetColor("_BaseColor") : 
                                 (instanceMat.HasProperty("_Color") ? instanceMat.GetColor("_Color") : Color.black);
                    Color sEmis = instanceMat.HasProperty("_EmissionColor") ? instanceMat.GetColor("_EmissionColor") : Color.black;
                    
                    if (enableEmission) instanceMat.EnableKeyword("_EMISSION");
                    
                    matsToFade.Add((instanceMat, sCol, sEmis));
                }
            }
        }

        if (matsToFade.Count == 0) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            foreach (var fadeData in matsToFade)
            {
                Material m = fadeData.mat;
                Color curCol = Color.Lerp(fadeData.startColor, targetColor, t);
                Color curEmis = Color.Lerp(fadeData.startEmis, enableEmission ? targetColor : Color.black, t);

                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", curCol);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", curCol);

                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", curEmis);
            }
            yield return null;
        }

        // Finalize
        foreach (var fadeData in matsToFade)
        {
            Material m = fadeData.mat;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", targetColor);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", targetColor);

            if (enableEmission)
            {
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", targetColor);
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            }
        }
    }

    private void ApplyEngineGlowEffect(Color targetColor, bool enableEmission)
    {
        if (spawnedEngine == null || engineGlowMaterial == null) return;

        Renderer[] allRenderers = spawnedEngine.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in allRenderers)
        {
            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                Material sharedMat = r.sharedMaterials[i];
                if (sharedMat != null && (sharedMat == engineGlowMaterial || sharedMat.name.StartsWith(engineGlowMaterial.name)))
                {
                    Material instanceMat = r.materials[i]; // Get instance material safely
                    
                    if (instanceMat.HasProperty("_BaseColor"))
                        instanceMat.SetColor("_BaseColor", targetColor);
                    else if (instanceMat.HasProperty("_Color"))
                        instanceMat.SetColor("_Color", targetColor);

                    if (enableEmission)
                    {
                        instanceMat.EnableKeyword("_EMISSION");
                        if (instanceMat.HasProperty("_EmissionColor"))
                            instanceMat.SetColor("_EmissionColor", targetColor);
                    }
                    else
                    {
                        instanceMat.DisableKeyword("_EMISSION");
                        if (instanceMat.HasProperty("_EmissionColor"))
                            instanceMat.SetColor("_EmissionColor", Color.black);
                    }
                }
            }
        }
    }

    private IEnumerator FadeEngineOuterBodies(float targetAlpha, float duration)
    {
        if (spawnedEngine == null || engineTransparentMaterials == null || engineTransparentMaterials.Count == 0) yield break;

        List<(Material mat, string prop, Color startColor)> fadeMats = new List<(Material, string, Color)>();
        HashSet<Material> processedMats = new HashSet<Material>();
        List<Renderer> fadedRenderers = new List<Renderer>();

        Renderer[] allRenderers = spawnedEngine.GetComponentsInChildren<Renderer>(true);
        
        foreach (Renderer r in allRenderers)
        {
            bool hasMatchingMaterial = false;
            
            // Check if this renderer uses any of the targeted materials
            for (int i = 0; i < r.sharedMaterials.Length; i++)
            {
                Material sharedMat = r.sharedMaterials[i];
                if (sharedMat == null) continue;

                // Match by reference or by name
                bool match = false;
                foreach (Material targetMat in engineTransparentMaterials)
                {
                    if (targetMat != null && (sharedMat == targetMat || sharedMat.name.StartsWith(targetMat.name)))
                    {
                        match = true;
                        break;
                    }
                }

                if (match)
                {
                    hasMatchingMaterial = true;
                    Material instanceMat = r.materials[i]; // Get instance material safely
                    
                    if (instanceMat != null && !processedMats.Contains(instanceMat))
                    {
                        string colorProp = instanceMat.HasProperty("_BaseColor") ? "_BaseColor" : (instanceMat.HasProperty("_Color") ? "_Color" : string.Empty);
                        if (!string.IsNullOrEmpty(colorProp))
                        {
                            ConfigureMaterialForFade(instanceMat);
                            fadeMats.Add((instanceMat, colorProp, instanceMat.GetColor(colorProp)));
                            processedMats.Add(instanceMat);
                        }
                    }
                }
            }
            
            if (hasMatchingMaterial)
            {
                fadedRenderers.Add(r);
            }
        }

        if (fadeMats.Count == 0) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            for (int i = 0; i < fadeMats.Count; i++)
            {
                var entry = fadeMats[i];
                Color c = entry.startColor;
                c.a = Mathf.Lerp(entry.startColor.a, targetAlpha, t);
                entry.mat.SetColor(entry.prop, c);
            }

            yield return null;
        }
        
        // Hide completely if target alpha is very close to 0
        if (targetAlpha <= 0.01f)
        {
            foreach (Renderer r in fadedRenderers)
            {
                if (r != null) r.enabled = false;
            }
        }
    }

    private void ApplyLocalOilEffect()
    {
        UpdateStatus("<color=#F87171>●</color>  Friction Detected");

        SetEngineLoopClip(normalOilEngineLoop);

        // Ensure local oil uses prefab-default smoke, not prior BPCL tint.
        RestoreEngineSmokeDefaultStartColors();
        
        // Play all smoke particles from engine prefab with default colors (bad oil = full smoke)
        PlayEngineSmokeParticles(null, null);
        
        // Trigger vibrate animation
        TriggerEngineAnimation("Vibrate");

        // Vibrate engine prefab transform
        StartCoroutine(ShakeEngineRoutine(2.5f, 0.015f));
        
        // Show demerits panel
        ShowLocalOilDemerits();

        // FADE ENGINE OUTER BODY TO REVEAL INSIDE
        StartCoroutine(FadeEngineOuterBodies(engineBodyTransparentAlpha, engineBodyFadeDuration));

        // TURN TARGET GLOW MATERIAL BLACK SMOOTHLY
        StartCoroutine(FadeEngineGlowEffect(normalOilDarkColor, false, engineGlowTransitionDuration));
    }
    
    private IEnumerator ShakeEngineRoutine(float duration, float maxIntensity)
    {
        if (spawnedEngine == null) yield break;

        Vector3 originalPos = spawnedEngine.transform.localPosition;
        float elapsed = 0f;
        float fadeInTime = 0.4f;
        float fadeOutTime = 0.6f;

        while (elapsed < duration)
        {
            float intensityMultiplier = 1f;

            // Fade in vibration
            if (elapsed < fadeInTime)
                intensityMultiplier = elapsed / fadeInTime;
            // Fade out vibration
            else if (elapsed > duration - fadeOutTime)
                intensityMultiplier = (duration - elapsed) / fadeOutTime;

            float currentIntensity = maxIntensity * intensityMultiplier * intensityMultiplier; // Ease in-out curve

            float xOffset = UnityEngine.Random.Range(-1f, 1f) * currentIntensity;
            float zOffset = UnityEngine.Random.Range(-1f, 1f) * currentIntensity;
            
            spawnedEngine.transform.localPosition = originalPos + new Vector3(xOffset, 0f, zOffset);
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        if (spawnedEngine != null)
        {
            spawnedEngine.transform.localPosition = originalPos;
        }
    }
    
    private void TriggerEngineAnimation(string triggerName)
    {
        Animator animator = GetEngineAnimator();
        if (animator != null)
        {
            animator.SetTrigger(triggerName);
            Debug.Log($"[AR_Oil] Triggered animation: {triggerName}");
        }
    }

    private Animator GetEngineAnimator()
    {
        if (cachedEngineAnimator != null)
            return cachedEngineAnimator;

        if (spawnedEngine == null)
            return null;

        cachedEngineAnimator = spawnedEngine.GetComponentInChildren<Animator>();
        return cachedEngineAnimator;
    }

    private void SetEngineVibrationForPouring(bool enabled)
    {
        Animator animator = GetEngineAnimator();
        if (animator == null)
            return;

        if (enabled)
        {
            animator.SetTrigger("Vibrate");
        }

        SetAnimatorBoolIfExists(animator, "IsVibrating", enabled);
        SetAnimatorBoolIfExists(animator, "Vibrate", enabled);
        SetAnimatorFloatIfExists(animator, "VibrateBlend", enabled ? 1f : 0f);
    }

    private void SetAnimatorBoolIfExists(Animator animator, string parameterName, bool value)
    {
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == parameterName && p.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(parameterName, value);
                return;
            }
        }
    }

    private void SetAnimatorFloatIfExists(Animator animator, string parameterName, float value)
    {
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == parameterName && p.type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(parameterName, value);
                return;
            }
        }
    }
    
    private void ResetEngineState()
    {
        Debug.Log("[AR_Oil] Resetting engine state");

        if (oilVFXController != null) oilVFXController.OnReset();
        if (oilPourManager != null) oilPourManager.OnReset();

        // DEACTIVATE BUBBLES IF PRESENT
        if (spawnedEngine != null)
        {
            EngineSimulation.PistonOilParticleController bubblesController = spawnedEngine.GetComponentInChildren<EngineSimulation.PistonOilParticleController>(true);
            if (bubblesController != null)
            {
                bubblesController.DeactivateOilBubbles();
            }
            else
            {
                // Fallback deactivation if script missing
                ParticleSystem[] allPS = spawnedEngine.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in allPS)
                {
                    if (ps.CompareTag("OilBubble") || ps.gameObject.name.Contains("OilBubble"))
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        ps.gameObject.SetActive(false);
                    }
                }
            }
        }

        SetEngineVibrationForPouring(false);
        SetEngineLoopClip(defaultEngineLoop);
        
        // Reset engine animation
        TriggerEngineAnimation("Idle");
        
        // Stop all smoke particles (engine prefab + standalone)
        StopEngineSmokeParticles();
        RestoreEngineSmokeDefaultStartColors();
        
        // Hide panels
        HideResult();
        HideARBenefitsPanel();
        SetBottomActivityVisible(false);
        
        lastPouredOil = OilType.None;
    }
    
    #endregion

    #region === BENEFITS PANELS ===
    
    private void ShowBPCLBenefits()
    {
        string benefits = @"<b>MAK Supreme</b>
<size=80%><color=#4ADE80>Synthetic</color></size>

<color=#86EFAC>✓</color> More Mileage
<color=#86EFAC>✓</color> Long Life
<color=#86EFAC>✓</color> All Weather
<color=#86EFAC>✓</color> Smooth Start";

        benefits += GetScoreInlineText(OilType.BPCL);
        benefits += GetComparisonInlineText();

        CreateARBenefitsPanel(benefits, new Color(0.08f, 0.15f, 0.12f, 0.95f), true);
    }
    
    private void ShowLocalOilDemerits()
    {
        string demerits = @"<b>Mineral Lube</b>
<size=80%><color=#F87171>Standard</color></size>

<color=#FCA5A5>✗</color> Less Mileage
<color=#FCA5A5>✗</color> Short Life
<color=#FCA5A5>✗</color> Limited Use
<color=#FCA5A5>✗</color> Slow Start";

        demerits += GetScoreInlineText(OilType.Local);
        demerits += GetComparisonInlineText();

        CreateARBenefitsPanel(demerits, new Color(0.18f, 0.08f, 0.08f, 0.95f), false);
    }

    private string GetScoreInlineText(OilType oil)
    {
        return string.Empty;
    }

    private string GetComparisonInlineText()
    {
        if (!enablePourScoring || !hasCompletedBPCL || !hasCompletedLocal)
            return string.Empty;

        int delta = bpclScore.FinalScore - localScore.FinalScore;
        string winner = delta >= 0 ? "MAK" : "Local";
        int margin = Mathf.Abs(delta);

        return $"\n<size=74%><color=#C4B5FD>Compare:</color> {winner} leads by {margin} pts</size>";
    }
    
    /// <summary>
    /// Creates an AR Benefits Panel anchored in world space next to the engine.
    /// Panel is fixed in AR world - does NOT follow camera.
    /// 
    /// Architecture: Single Responsibility - only creates and positions panel
    /// </summary>
    /// <param name="text">Content to display</param>
    /// <param name="bgColor">Background color</param>
    /// <param name="isPositive">True for benefits (green), false for demerits (red)</param>
    private void CreateARBenefitsPanel(string text, Color bgColor, bool isPositive)
    {
        HideARBenefitsPanel();
        
        if (spawnedEngine == null) 
        {
            Debug.LogWarning("[AR_Oil] Cannot create benefits panel - engine not spawned");
            return;
        }
        
        // === STEP 1: Create panel as INDEPENDENT world object (not parented to engine) ===
        // This prevents any engine transform changes from affecting the panel
        arBenefitsPanel = new GameObject("AR_Benefits_Panel");
        
        // === STEP 2: Calculate FIXED world position ===
        Vector3 enginePos = spawnedEngine.transform.position;
        
        // Get direction from engine to camera (at creation time only)
        Vector3 toCamera = arCamera.transform.position - enginePos;
        toCamera.y = 0; // Keep horizontal
        toCamera.Normalize();
        
        // Calculate right direction (perpendicular to camera view)
        Vector3 rightDir = Vector3.Cross(Vector3.up, toCamera).normalized;
        
        // Position panel to the RIGHT of engine, slightly elevated
        Vector3 panelWorldPos = enginePos 
            + rightDir * panelDistanceFromEngine 
            + Vector3.up * panelHeightOffset;
        
        arBenefitsPanel.transform.position = panelWorldPos;
        
        // === STEP 3: Face camera (ONE-TIME rotation, fixed in world) ===
        Vector3 lookDir = arCamera.transform.position - panelWorldPos;
        lookDir.y = 0; // Keep upright
        if (lookDir.sqrMagnitude > 0.001f)
        {
            // Panel faces TOWARD camera (text readable)
            arBenefitsPanel.transform.rotation = Quaternion.LookRotation(lookDir.normalized) * Quaternion.Euler(panelFacingRotationOffset);
        }
        
        // === STEP 4: Setup World Space Canvas ===
        Canvas canvas = arBenefitsPanel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        
        // Get canvas RectTransform for proper sizing
        RectTransform canvasRect = arBenefitsPanel.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(200, 150); // Canvas size in units
        
        // === STEP 5: Apply world scale ===
        // Scale determines real-world size: 0.001 = 1mm per canvas unit
        arBenefitsPanel.transform.localScale = Vector3.one * arPanelScale;
        
        // === STEP 6: Create UI elements ===
        CreatePanelBackground(bgColor, isPositive);
        CreatePanelText(text);
        
        // Ensure proper rendering
        SetLayerRecursively(arBenefitsPanel, LayerMask.NameToLayer("Default"));
        
        Debug.Log($"[AR_Oil] AR Benefits panel created - Position: {panelWorldPos}, Scale: {arPanelScale}, Fixed in world space");
    }
    
    /// <summary>
    /// Creates the panel background with border and accent
    /// </summary>
    private void CreatePanelBackground(Color bgColor, bool isPositive)
    {
        // Background container
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(arBenefitsPanel.transform, false);
        
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = bgColor;
        
        // Subtle border
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
            ? new Color(0.3f, 0.7f, 0.4f, 0.6f)  // Green border
            : new Color(0.7f, 0.3f, 0.3f, 0.6f); // Red border
        
        // Top accent line
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
            ? new Color(0.29f, 0.87f, 0.5f, 1f)   // Mint green
            : new Color(0.97f, 0.45f, 0.45f, 1f); // Coral red
    }
    
    /// <summary>
    /// Creates the panel text content
    /// </summary>
    private void CreatePanelText(string text)
    {
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(arBenefitsPanel.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 8);
        textRect.offsetMax = new Vector2(-10, -12);
        
        TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 12;  // Readable size
        tmp.color = new Color(0.95f, 0.97f, 1f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.lineSpacing = -5f;  // Tighter line spacing
        tmp.enableWordWrapping = true;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }
    
    private void HideARBenefitsPanel()
    {
        if (arBenefitsPanel != null)
        {
            Destroy(arBenefitsPanel);
            arBenefitsPanel = null;
        }
    }
    
    #endregion

    #region === UI CREATION ===
    
    private void CreateUI()
    {
        // Create Canvas
        GameObject canvasObj = new GameObject("AR_Oil_Canvas");
        uiCanvas = canvasObj.AddComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        uiCanvas.sortingOrder = 100;
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // === TOP STATUS BAR (Frosted Glass Effect - Enhanced) ===
        // Moved down to avoid notch area
        GameObject topBar = CreateMinimalPanel(canvasObj.transform, "TopBar",
            new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0, -80), new Vector2(0, 140), // Moved down by 80px
            new Color(0.08f, 0.1f, 0.15f, 0.95f));
            
        // Add subtle bottom border to top bar
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

        // Make top bar interactive (clickable)
        topBar.GetComponent<Image>().raycastTarget = true;
        Button topBarBtn = topBar.AddComponent<Button>();
        ColorBlock btnColors = topBarBtn.colors;
        btnColors.normalColor = Color.white;
        btnColors.highlightedColor = new Color(0.9f, 0.95f, 1f, 1f);
        btnColors.pressedColor = new Color(0.8f, 0.85f, 0.95f, 1f);
        btnColors.selectedColor = Color.white;
        topBarBtn.colors = btnColors;
        
        // Add a click listener to trigger the text pulse effect
        topBarBtn.onClick.AddListener(() => {
            if (instructionText != null) {
                StartCoroutine(PulseTextEffect(instructionText, 1.25f, 0.3f));
            }
        });
        
        // Status Text - Bolder, better contrast
        statusText = CreateMinimalText(topBar.transform, "StatusText", 
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -55), new Vector2(800, 50),
            "●  Initializing", 34, TextAlignmentOptions.Center,
            new Color(1f, 0.98f, 0.95f, 1f));
        statusText.fontStyle = FontStyles.Bold;
        
        // Add drop shadow
        UnityEngine.UI.Shadow stShadow = statusText.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        stShadow.effectColor = new Color(0, 0, 0, 0.6f);
        stShadow.effectDistance = new Vector2(1, -1);
        
        // Instruction Text - Larger, better contrast
        instructionText = CreateMinimalText(topBar.transform, "InstructionText",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -100), new Vector2(900, 40),
            "", 24, TextAlignmentOptions.Center,
            new Color(0.8f, 0.9f, 1f, 1f));
            
        // Outline for instruction text to guarantee readability across all backgrounds
        UnityEngine.UI.Outline instOutline = instructionText.gameObject.AddComponent<UnityEngine.UI.Outline>();
        instOutline.effectColor = new Color(0, 0, 0, 0.7f);
        instOutline.effectDistance = new Vector2(1, -1);
        
        // === POUR PROGRESS (Bottom - Pill Shape) ===
        // This is shown when HOLDING a bottle, before the pour starts
        GameObject pourContainer = new GameObject("PourContainer");
        pourContainer.transform.SetParent(canvasObj.transform, false);
        RectTransform pourContainerRect = pourContainer.AddComponent<RectTransform>();
        pourContainerRect.anchorMin = new Vector2(0.5f, 0);
        pourContainerRect.anchorMax = new Vector2(0.5f, 0);
        pourContainerRect.pivot = new Vector2(0.5f, 0);
        pourContainerRect.anchoredPosition = new Vector2(0, 200); // Moved up to avoid overlap
        pourContainerRect.sizeDelta = new Vector2(320, 50);
        
        pourSlider = CreateMinimalSlider(pourContainer.transform, "PourSlider",
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        pourContainer.SetActive(false);

        // Activity capsule shown only while POURING.
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
        
        // Store reference for showing/hiding
        pourSlider.transform.parent.gameObject.SetActive(false);
        
        // === RESULT PANEL (Center - Glass Card) ===
        resultPanel = CreateGlassPanel(canvasObj.transform, "ResultPanel",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 120), new Vector2(380, 100), // Moved up, resized
            new Color(0.08f, 0.1f, 0.12f, 0.92f));
        
        // Add subtle border
        AddPanelBorder(resultPanel, new Color(0.3f, 0.4f, 0.5f, 0.4f), 1.5f);
        
        resultText = CreateMinimalText(resultPanel.transform, "ResultText",
            new Vector2(0, 0), new Vector2(1, 1),
            Vector2.zero, new Vector2(-30, -20),
            "", 22, TextAlignmentOptions.Center, // Increased font size
            Color.white);
        resultText.fontStyle = FontStyles.Bold;
        
        resultPanel.SetActive(false);
        
        // --- ADD SPLASH SCREEN ---
        CreateSplashScreenUI(canvasObj.GetComponent<RectTransform>());

        Debug.Log("[AR_Oil] Minimalist UI created");
    }

    private void CreateSplashScreenUI(RectTransform canvasRect)
    {
        // 1) Main fullscreen panel
        splashScreenPanel = new GameObject("SplashScreenPanel");
        splashScreenPanel.transform.SetParent(canvasRect.transform, false);
        
        RectTransform splashRect = splashScreenPanel.AddComponent<RectTransform>();
        splashRect.anchorMin = Vector2.zero;
        splashRect.anchorMax = Vector2.one;
        splashRect.sizeDelta = Vector2.zero;
        splashRect.anchoredPosition = Vector2.zero;
        
        Image bgImg = splashScreenPanel.AddComponent<Image>();
        bgImg.color = new Color(0.95f, 0.95f, 0.95f, 1f); // White/off-white background
        
        // 2) Animated Canvas Group for fade out
        splashCanvasGroup = splashScreenPanel.AddComponent<CanvasGroup>();
        splashCanvasGroup.alpha = 1f;
        
        // 3) Vertical Layout Group for centering everything
        VerticalLayoutGroup layout = splashScreenPanel.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.spacing = 60f;
        
        // 4) Create Logo Image
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
            Debug.LogWarning("[AR_Oil] bpclLogoSprite is not assigned in the inspector!");
            logoImg.color = Color.blue; // Fallback visual
        }
        
        RectTransform logoRect = logoObj.GetComponent<RectTransform>();
        logoRect.sizeDelta = new Vector2(400, 400); // 400x400 max logo size

        // 5) Main Title Text
        GameObject titleObj = new GameObject("WelcomeTitle");
        titleObj.transform.SetParent(splashScreenPanel.transform, false);
        TextMeshProUGUI titleTxt = titleObj.AddComponent<TextMeshProUGUI>();
        titleTxt.text = "AR Engine Oil Comparison";
        titleTxt.fontSize = 60;
        titleTxt.fontStyle = FontStyles.Bold;
        titleTxt.alignment = TextAlignmentOptions.Center;
        titleTxt.color = new Color(0.1f, 0.2f, 0.5f, 1f); // Dark blue BPCL style
        
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.sizeDelta = new Vector2(700, 150); // width 700, height 150 for wrapping
        
        // 6) Description/Benefits Text
        GameObject descObj = new GameObject("DescriptionText");
        descObj.transform.SetParent(splashScreenPanel.transform, false);
        TextMeshProUGUI descTxt = descObj.AddComponent<TextMeshProUGUI>();
        descTxt.text = "Experience the difference in engine protection\nand performance.\n\n• Advanced Wear Protection\n• Superior Heat Resistance\n• Enhanced Engine Life";
        descTxt.fontSize = 36;
        descTxt.alignment = TextAlignmentOptions.Center;
        descTxt.color = new Color(0.3f, 0.3f, 0.3f, 1f); // Dark gray
        
        RectTransform descRect = descObj.GetComponent<RectTransform>();
        descRect.sizeDelta = new Vector2(800, 300); // Give it some width for wrapping

        // Start active
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
        
        // Round corners effect via child mask (simplified)
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
        
        // Use outline instead of separate border image
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

    private IEnumerator PulseTextEffect(TextMeshProUGUI tmp, float scaleMultiplier, float duration)
    {
        if (tmp == null) yield break;
        
        Vector3 origScale = tmp.rectTransform.localScale;
        Vector3 targetScale = origScale * scaleMultiplier;
        
        float halfDur = duration / 2f;
        float elapsed = 0f;
        
        // Scale up
        while (elapsed < halfDur)
        {
            elapsed += Time.deltaTime;
            tmp.rectTransform.localScale = Vector3.Lerp(origScale, targetScale, elapsed / halfDur);
            yield return null;
        }
        
        // Scale down
        elapsed = 0f;
        while (elapsed < halfDur)
        {
            elapsed += Time.deltaTime;
            tmp.rectTransform.localScale = Vector3.Lerp(targetScale, origScale, elapsed / halfDur);
            yield return null;
        }
        
        tmp.rectTransform.localScale = origScale;
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
        
        // Background - Pill shape track
        GameObject bg = new GameObject("Track");
        bg.transform.SetParent(obj.transform, false);
        RectTransform bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0, 0.3f);
        bgRect.anchorMax = new Vector2(1, 0.7f);
        bgRect.offsetMin = new Vector2(8, 0);
        bgRect.offsetMax = new Vector2(-8, 0);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.15f, 0.18f, 0.22f, 0.9f);
        
        // Fill Area
        GameObject fillArea = new GameObject("FillArea");
        fillArea.transform.SetParent(obj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0, 0.3f);
        fillAreaRect.anchorMax = new Vector2(1, 0.7f);
        fillAreaRect.offsetMin = new Vector2(8, 2);
        fillAreaRect.offsetMax = new Vector2(-8, -2);
        
        // Fill - Gradient-like green
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
        fillImg.color = new Color(0.2f, 0.85f, 0.5f, 1f); // Mint green
        
        // Label
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
    
    private void UpdateStatus(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
        Debug.Log($"[AR_Oil] Status: {text}");
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

    private void SetBottomActivityState(string title, string detail, string phaseIcon)
    {
        if (bottomActivityPanel == null)
            return;

        if (bottomActivityTitleText != null)
            bottomActivityTitleText.text = title;

        if (bottomActivityDetailText != null)
            bottomActivityDetailText.text = detail;

        if (bottomActivityPhaseIconText != null)
            bottomActivityPhaseIconText.text = phaseIcon;

        SetBottomActivityVisible(true);
    }

    private void SetBottomActivityVisible(bool visible)
    {
        if (bottomActivityPanel != null)
            bottomActivityPanel.SetActive(visible);
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
    
    private void HideResult()
    {
        if (resultPanel != null)
        {
            resultPanel.SetActive(false);
        }
    }

    private string GetGuidedInstruction(InteractionState state)
    {
        if (!enableGuidedOnboarding)
        {
            return state switch
            {
                InteractionState.WaitingForPlane => "Point at a flat surface",
                InteractionState.Holding => "Tilt forward to pour",
                InteractionState.Pouring => "Keep tilting...",
                InteractionState.PourComplete => "Tap another bottle to compare",
                _ => "Tap a bottle to select"
            };
        }

        if (!hasSpawned || state == InteractionState.WaitingForPlane)
            return "Step 1/4  Scan a flat surface";

        if (!hasSelectedAnyBottle)
            return "Step 2/4  Tap a bottle to begin";

        if (state == InteractionState.Holding)
            return "Step 3/4  Tilt phone forward to start pour";

        if (state == InteractionState.Pouring)
            return "Step 3/4  Keep steady tilt for best score";

        if (state == InteractionState.PourComplete)
            return "Step 4/4  Drag to rotate, pinch to zoom, tap another bottle to compare";

        if (hasCompletedBPCL && hasCompletedLocal)
            return "Step 4/4  Comparison ready. Tap any bottle to retry";

        if (hasCompletedBPCL || hasCompletedLocal)
            return "Step 4/4  Tap the other bottle to compare";

        return "Step 2/4  Tap a bottle to begin";
    }

    private void BeginPourScoreTracking()
    {
        if (!enablePourScoring)
            return;

        activePourStartTime = Time.time;
        activeTiltErrorAccum = 0f;
        activeTiltSamples = 0;
        activeTiltDrops = 0;
        activeTiltBelowThreshold = false;
    }

    private void TrackPourScoreSample(float absTilt)
    {
        float targetTilt = Mathf.Clamp01((tiltStartThreshold + tiltContinueThreshold) * 0.5f);
        float normalization = Mathf.Max(0.15f, 1f - tiltContinueThreshold);
        float normalizedError = Mathf.Clamp01(Mathf.Abs(absTilt - targetTilt) / normalization);

        activeTiltErrorAccum += normalizedError;
        activeTiltSamples++;

        bool belowThreshold = absTilt < tiltContinueThreshold;
        if (belowThreshold && !activeTiltBelowThreshold)
        {
            activeTiltDrops++;
        }
        activeTiltBelowThreshold = belowThreshold;
    }

    private void RecordCompletedPourScore(OilType oil)
    {
        PourScore score = GetPourScore(oil);
        if (score == null)
            return;

        score.Completed = true;
        score.DurationSeconds = Mathf.Max(0.01f, Time.time - activePourStartTime);
        score.TiltDrops = activeTiltDrops;

        float avgError = activeTiltSamples > 0 ? activeTiltErrorAccum / activeTiltSamples : 1f;
        score.StabilityScore = Mathf.RoundToInt(Mathf.Clamp01(1f - avgError) * 100f);
        score.ControlScore = Mathf.Clamp(100 - activeTiltDrops * 18, 25, 100);
        float timeEfficiency = Mathf.InverseLerp(pourDuration * 2f, pourDuration * 0.7f, score.DurationSeconds);
        score.TimeScore = Mathf.RoundToInt(Mathf.Clamp01(timeEfficiency) * 100f);

        score.FinalScore = Mathf.RoundToInt(
            score.StabilityScore * 0.45f +
            score.ControlScore * 0.35f +
            score.TimeScore * 0.20f
        );

        if (oil == OilType.BPCL) hasCompletedBPCL = true;
        if (oil == OilType.Local) hasCompletedLocal = true;
    }

    private PourScore GetPourScore(OilType oil)
    {
        return oil switch
        {
            OilType.BPCL => bpclScore,
            OilType.Local => localScore,
            _ => null
        };
    }

    #endregion

    #region === VISUAL EFFECTS ===
    
    private void ApplyHighlight(GameObject obj)
    {
        if (obj == null) return;
        
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            if (!originalColors.ContainsKey(renderer))
            {
                // Store original colors
                Color[] colors = new Color[renderer.materials.Length];
                for (int i = 0; i < renderer.materials.Length; i++)
                {
                    if (renderer.materials[i].HasProperty("_Color"))
                        colors[i] = renderer.materials[i].color;
                    else if (renderer.materials[i].HasProperty("_BaseColor"))
                        colors[i] = renderer.materials[i].GetColor("_BaseColor");
                    else
                        colors[i] = Color.white;
                }
                originalColors[renderer] = colors;
            }
            
            // Apply highlight
            foreach (var mat in renderer.materials)
            {
                if (mat.HasProperty("_Color"))
                    mat.color = selectionHighlightColor;
                else if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", selectionHighlightColor);
                    
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", selectionHighlightColor * 0.5f);
                }
            }
        }
    }
    
    private void RemoveHighlight(GameObject obj)
    {
        if (obj == null) return;
        
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            if (originalColors.TryGetValue(renderer, out Color[] colors))
            {
                for (int i = 0; i < renderer.materials.Length && i < colors.Length; i++)
                {
                    if (renderer.materials[i].HasProperty("_Color"))
                        renderer.materials[i].color = colors[i];
                    else if (renderer.materials[i].HasProperty("_BaseColor"))
                        renderer.materials[i].SetColor("_BaseColor", colors[i]);
                        
                    if (renderer.materials[i].HasProperty("_EmissionColor"))
                    {
                        renderer.materials[i].SetColor("_EmissionColor", Color.black);
                    }
                }
            }
        }
    }
    
    private IEnumerator AnimateBottleReturn(GameObject bottle, Vector3 targetPos, Quaternion targetRot)
    {
        if (bottle == null) yield break;
        isBottleReturnAnimating = true;

        try
        {
            Vector3 startPos = bottle.transform.position;
            Quaternion startRot = bottle.transform.rotation;

            float duration = 0.6f;
            float elapsed = 0f;

            if (spawnedEngine != null && hasCachedEngineTransform)
            {
                spawnedEngine.transform.SetPositionAndRotation(spawnedEngineOriginalPosition, spawnedEngineOriginalRotation);
                spawnedEngine.transform.localScale = spawnedEngineOriginalScale;
            }

            ResetEngineManipulationState();

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = EaseOutQuad(elapsed / duration);

                bottle.transform.position = Vector3.Lerp(startPos, targetPos, t);
                bottle.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

                yield return null;
            }

            bottle.transform.position = targetPos;
            bottle.transform.rotation = targetRot;
        }
        finally
        {
            isBottleReturnAnimating = false;
        }
    }
    
    #endregion

    #region === UTILITY METHODS ===
    
    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    private void SetEngineLoopClip(AudioClip clip)
    {
        if (activeEngineSource == null)
            return;

        if (audioCrossfadeCoroutine != null)
        {
            StopCoroutine(audioCrossfadeCoroutine);
            audioCrossfadeCoroutine = null;
        }

        activeEngineSource.volume = engineLoopVolume;

        if (clip == null)
        {
            activeEngineSource.Stop();
            activeEngineSource.clip = null;
            return;
        }

        if (activeEngineSource.clip == clip && activeEngineSource.isPlaying)
            return;

        activeEngineSource.Stop();
        activeEngineSource.clip = clip;
        activeEngineSource.Play();

        AudioSource inactiveSource = activeEngineSource == engineAudioSource ? engineAudioSource2 : engineAudioSource;
        if (inactiveSource != null)
        {
            inactiveSource.Stop();
            inactiveSource.volume = 0f;
        }
    }

    private void CrossfadeEngineLoopClip(AudioClip clip, float duration)
    {
        if (activeEngineSource == null || engineAudioSource2 == null) return;
        
        if (audioCrossfadeCoroutine != null)
        {
            StopCoroutine(audioCrossfadeCoroutine);
        }
        
        if (clip == null)
        {
            SetEngineLoopClip(null);
            return;
        }

        if (activeEngineSource.clip == clip && activeEngineSource.isPlaying)
            return;
            
        audioCrossfadeCoroutine = StartCoroutine(CrossfadeAudioRoutine(clip, duration));
    }

    private IEnumerator CrossfadeAudioRoutine(AudioClip targetClip, float duration)
    {
        AudioSource fadingOutSource = activeEngineSource;
        AudioSource fadingInSource = activeEngineSource == engineAudioSource ? engineAudioSource2 : engineAudioSource;
        
        fadingInSource.clip = targetClip;
        fadingInSource.volume = 0f;
        fadingInSource.Play();
        
        activeEngineSource = fadingInSource;
        
        float startOutVol = fadingOutSource.volume;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            fadingOutSource.volume = Mathf.Lerp(startOutVol, 0f, t);
            fadingInSource.volume = Mathf.Lerp(0f, engineLoopVolume, t);
            
            yield return null;
        }
        
        fadingOutSource.volume = 0f;
        fadingOutSource.Stop();
        fadingInSource.volume = engineLoopVolume;
        
        audioCrossfadeCoroutine = null;
    }
    
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (obj == null) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
    
    private void EnsureColliders(GameObject obj)
    {
        if (obj == null) return;
        
        Collider[] colliders = obj.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
        {
            // Add box collider based on renderers
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (var r in renderers)
                {
                    bounds.Encapsulate(r.bounds);
                }
                
                BoxCollider box = obj.AddComponent<BoxCollider>();
                box.center = obj.transform.InverseTransformPoint(bounds.center);
                box.size = bounds.size / obj.transform.lossyScale.x;
            }
        }
    }

    private void StartCarModelFadeIfConfigured()
    {
        GameObject target = ResolveCarFadeTarget();
        if (target == null)
            return;

        if (carFadeDuration <= 0.01f)
            return;

        float minDelay = Mathf.Max(0f, Mathf.Min(carFadeDelayMin, carFadeDelayMax));
        float maxDelay = Mathf.Max(minDelay, Mathf.Max(carFadeDelayMin, carFadeDelayMax));
        float delay = UnityEngine.Random.Range(minDelay, maxDelay);

        if (carFadeCoroutine != null)
        {
            StopCoroutine(carFadeCoroutine);
        }

        carFadeCoroutine = StartCoroutine(FadeOutObjectAfterDelay(target, delay, carFadeDuration));
        Debug.Log($"[AR_Oil] Car fade scheduled for {target.name} in {delay:F1}s (duration {carFadeDuration:F1}s)");
    }

    private GameObject ResolveCarFadeTarget()
    {
        if (carModelToFade != null && carModelToFade.scene.IsValid())
            return carModelToFade;

        if (spawnedEngine != null && spawnedEngine.transform.parent != null)
            return spawnedEngine.transform.parent.gameObject;

        return spawnedEngine;
    }

    private IEnumerator FadeOutObjectAfterDelay(GameObject target, float delay, float duration)
    {
        if (target == null)
            yield break;

        yield return new WaitForSeconds(delay);

        if (target == null)
            yield break;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            yield break;

        Transform engineRoot = spawnedPersistentEngineRoot != null
            ? spawnedPersistentEngineRoot
            : (spawnedEngine != null ? spawnedEngine.transform : null);
        List<(Material mat, string prop, Color color)> fadeMats = new List<(Material, string, Color)>();
        HashSet<Renderer> fadedRenderers = new HashSet<Renderer>();

        foreach (Renderer renderer in renderers)
        {
            if (engineRoot != null && (renderer.transform == engineRoot || renderer.transform.IsChildOf(engineRoot)))
                continue;

            foreach (Material mat in renderer.materials)
            {
                if (mat == null)
                    continue;

                string colorProp = mat.HasProperty("_BaseColor") ? "_BaseColor" : (mat.HasProperty("_Color") ? "_Color" : string.Empty);
                if (string.IsNullOrEmpty(colorProp))
                    continue;

                ConfigureMaterialForFade(mat);
                fadeMats.Add((mat, colorProp, mat.GetColor(colorProp)));
                fadedRenderers.Add(renderer);
            }
        }

        if (fadeMats.Count == 0)
            yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            for (int i = 0; i < fadeMats.Count; i++)
            {
                var entry = fadeMats[i];
                Color c = entry.color;
                c.a = Mathf.Lerp(entry.color.a, 0f, t);
                entry.mat.SetColor(entry.prop, c);
            }

            yield return null;
        }

        foreach (Renderer renderer in fadedRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null)
                continue;

            if (engineRoot != null && (collider.transform == engineRoot || collider.transform.IsChildOf(engineRoot)))
                continue;

            collider.enabled = false;
        }

        carFadeCoroutine = null;
    }

    private void ConfigureMaterialForFade(Material mat)
    {
        if (mat == null)
            return;

        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_SrcBlend"))
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite"))
            mat.SetInt("_ZWrite", 0);

        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
    
    private void CleanupResources()
    {
        #if ENABLE_INPUT_SYSTEM
        if (EnhancedTouchSupport.enabled)
        {
            // Don't disable - may be used by other components
        }
        #endif

        if (carFadeCoroutine != null)
        {
            StopCoroutine(carFadeCoroutine);
            carFadeCoroutine = null;
        }

        if (engineAudioSource != null)
        {
            engineAudioSource.Stop();
        }
        
        bottlePourParticles.Clear();
        originalColors.Clear();
    }
    
    private void LogDebugState()
    {
        float tilt = GetTiltValue();
        Debug.Log($"[AR_Oil] State={currentState}, Selected={selectedOil}, Tilt={tilt:F3}, SmoothedTilt={smoothedTiltValue:F3}, Pour={pourProgress:F2}");
    }
    
    // Easing functions
    private float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    private float EaseInOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
    
    private Vector3 QuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * p1 + t * t * p2;
    }
    
    #endregion

    #region === PUBLIC API ===
    
    /// <summary>
    /// Get the current interaction state
    /// </summary>
    public InteractionState GetCurrentState() => currentState;
    
    /// <summary>
    /// Get the currently selected oil type
    /// </summary>
    public OilType GetSelectedOil() => selectedOil;
    
    /// <summary>
    /// Check if objects have been spawned
    /// </summary>
    public bool HasSpawned() => hasSpawned;
    
    /// <summary>
    /// Get spawned engine reference
    /// </summary>
    public GameObject GetEngine() => spawnedEngine;
    
    /// <summary>
    /// Get spawned bottle reference
    /// </summary>
    public GameObject GetBottle() => GetBottle(selectedOil);
    
    #endregion
}

