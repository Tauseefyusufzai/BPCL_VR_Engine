using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Editor wizard to quickly set up the AR Oil Demo scene.
/// Access via Window > AR Oil Demo Setup
/// </summary>
public class AR_OilDemoSetupWizard : EditorWindow
{
    private GameObject enginePrefab;
    private GameObject bpclBottlePrefab;
    private GameObject localBottlePrefab;
    
    private bool createSampleBottles = true;
    private bool createParticles = true;
    private bool createUI = true;
    private bool createAudio = true;

    [MenuItem("Window/AR Oil Demo Setup")]
    public static void ShowWindow()
    {
        GetWindow<AR_OilDemoSetupWizard>("AR Oil Demo Setup");
    }

    private void OnGUI()
    {
        GUILayout.Label("AR Oil Demo Setup Wizard", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        // Prefab assignments
        EditorGUILayout.LabelField("Prefab References", EditorStyles.boldLabel);
        enginePrefab = (GameObject)EditorGUILayout.ObjectField("Engine Prefab", enginePrefab, typeof(GameObject), false);
        bpclBottlePrefab = (GameObject)EditorGUILayout.ObjectField("BPCL Bottle Prefab", bpclBottlePrefab, typeof(GameObject), false);
        localBottlePrefab = (GameObject)EditorGUILayout.ObjectField("Local Bottle Prefab", localBottlePrefab, typeof(GameObject), false);

        EditorGUILayout.Space(10);

        // Options
        EditorGUILayout.LabelField("Setup Options", EditorStyles.boldLabel);
        createSampleBottles = EditorGUILayout.Toggle("Create Sample Bottles (if none)", createSampleBottles);
        createParticles = EditorGUILayout.Toggle("Create Particle Systems", createParticles);
        createUI = EditorGUILayout.Toggle("Create UI Canvas", createUI);
        createAudio = EditorGUILayout.Toggle("Add Audio Source", createAudio);

        EditorGUILayout.Space(20);

        // Setup buttons
        if (GUILayout.Button("Setup AR Session (if missing)", GUILayout.Height(30)))
        {
            SetupARSession();
        }

        if (GUILayout.Button("Create AR_OilInteraction Manager", GUILayout.Height(30)))
        {
            CreateOilInteractionManager();
        }

        EditorGUILayout.Space(10);

        if (GUILayout.Button("FULL SETUP (All Components)", GUILayout.Height(40)))
        {
            FullSetup();
        }

        EditorGUILayout.Space(20);
        EditorGUILayout.HelpBox(
            "Instructions:\n" +
            "1. Assign your prefabs above (or let wizard create samples)\n" +
            "2. Click 'FULL SETUP' to create all required components\n" +
            "3. Build to Android and test on Samsung S23\n\n" +
            "The demo will:\n" +
            "- Auto-detect horizontal planes\n" +
            "- Spawn engine and oil bottles\n" +
            "- Allow touch selection\n" +
            "- Detect phone tilt for pouring",
            MessageType.Info);
    }

    private void SetupARSession()
    {
        // Check for existing AR Session
        ARSession existingSession = FindFirstObjectByType<ARSession>();
        if (existingSession != null)
        {
            Debug.Log("AR Session already exists in scene.");
            Selection.activeGameObject = existingSession.gameObject;
            return;
        }

        // Create AR Session
        GameObject arSession = new GameObject("AR Session");
        arSession.AddComponent<ARSession>();
        arSession.AddComponent<ARInputManager>();

        // Create AR Session Origin with required components
        GameObject arSessionOrigin = new GameObject("AR Session Origin");
        ARSessionOrigin origin = arSessionOrigin.AddComponent<ARSessionOrigin>();

        // Add AR Camera
        GameObject arCameraObj = new GameObject("AR Camera");
        arCameraObj.transform.SetParent(arSessionOrigin.transform);
        arCameraObj.tag = "MainCamera";
        
        Camera arCamera = arCameraObj.AddComponent<Camera>();
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        arCamera.backgroundColor = Color.black;
        arCamera.nearClipPlane = 0.1f;
        arCamera.farClipPlane = 20f;

        arCameraObj.AddComponent<ARCameraManager>();
        arCameraObj.AddComponent<ARCameraBackground>();
        
        // TrackedPoseDriver is optional - AR Camera will be tracked by AR Foundation
        // If using Input System package, you can add TrackedPoseDriver manually

        origin.camera = arCamera;

        // Add AR Plane Manager
        ARPlaneManager planeManager = arSessionOrigin.AddComponent<ARPlaneManager>();
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

        // Add AR Raycast Manager
        arSessionOrigin.AddComponent<ARRaycastManager>();

        Debug.Log("AR Session setup complete!");
        Selection.activeGameObject = arSessionOrigin;
    }

    private void CreateOilInteractionManager()
    {
        // Check if already exists
        AR_OilInteraction existing = FindFirstObjectByType<AR_OilInteraction>();
        if (existing != null)
        {
            Debug.Log("AR_OilInteraction already exists in scene.");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        // Create manager object
        GameObject manager = new GameObject("AR_OilInteraction_Manager");
        AR_OilInteraction interaction = manager.AddComponent<AR_OilInteraction>();

        // Assign prefabs via SerializedObject
        SerializedObject so = new SerializedObject(interaction);
        
        if (enginePrefab != null)
            so.FindProperty("enginePrefab").objectReferenceValue = enginePrefab;
        
        if (bpclBottlePrefab != null)
            so.FindProperty("bpclBottlePrefab").objectReferenceValue = bpclBottlePrefab;
        else if (createSampleBottles)
            so.FindProperty("bpclBottlePrefab").objectReferenceValue = CreateSampleBottle("BPCL_Bottle", new Color(0.2f, 0.6f, 0.2f));
        
        if (localBottlePrefab != null)
            so.FindProperty("localBottlePrefab").objectReferenceValue = localBottlePrefab;
        else if (createSampleBottles)
            so.FindProperty("localBottlePrefab").objectReferenceValue = CreateSampleBottle("Local_Bottle", new Color(0.6f, 0.2f, 0.2f));

        // Setup particles
        if (createParticles)
        {
            GameObject smokeParticles = CreateSmokeParticles("SmokeParticles", Color.gray);
            GameObject blackSmoke = CreateSmokeParticles("BlackSmokeParticles", Color.black);
            GameObject pourParticles = CreatePouringParticles();

            so.FindProperty("smokeParticles").objectReferenceValue = smokeParticles.GetComponent<ParticleSystem>();
            so.FindProperty("blackSmokeParticles").objectReferenceValue = blackSmoke.GetComponent<ParticleSystem>();
            so.FindProperty("pouringParticles").objectReferenceValue = pourParticles.GetComponent<ParticleSystem>();

            smokeParticles.transform.SetParent(manager.transform);
            blackSmoke.transform.SetParent(manager.transform);
            pourParticles.transform.SetParent(manager.transform);
        }

        // Add audio source
        if (createAudio)
        {
            AudioSource audioSource = manager.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
            so.FindProperty("audioSource").objectReferenceValue = audioSource;
        }

        so.ApplyModifiedProperties();

        // Create UI
        if (createUI)
        {
            CreateUICanvas(interaction);
        }

        Debug.Log("AR_OilInteraction Manager created!");
        Selection.activeGameObject = manager;
    }

    private void FullSetup()
    {
        SetupARSession();
        CreateOilInteractionManager();
        Debug.Log("Full AR Oil Demo setup complete!");
    }

    private GameObject CreateSampleBottle(string name, Color color)
    {
        // Create a simple bottle shape
        GameObject bottle = new GameObject(name);
        
        // Bottle body (cylinder)
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Body";
        body.transform.SetParent(bottle.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(0.3f, 0.5f, 0.3f);
        
        // Bottle neck (smaller cylinder)
        GameObject neck = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        neck.name = "Neck";
        neck.transform.SetParent(bottle.transform);
        neck.transform.localPosition = new Vector3(0, 0.6f, 0);
        neck.transform.localScale = new Vector3(0.15f, 0.2f, 0.15f);

        // Apply color
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        mat.SetFloat("_Smoothness", 0.8f);

        body.GetComponent<Renderer>().material = mat;
        neck.GetComponent<Renderer>().material = mat;

        // Create label
        GameObject label = GameObject.CreatePrimitive(PrimitiveType.Quad);
        label.name = "Label";
        label.transform.SetParent(bottle.transform);
        label.transform.localPosition = new Vector3(0, 0, 0.16f);
        label.transform.localScale = new Vector3(0.2f, 0.3f, 1f);
        
        Material labelMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        labelMat.color = Color.white;
        label.GetComponent<Renderer>().material = labelMat;
        Object.DestroyImmediate(label.GetComponent<Collider>());

        // Save as prefab
        string path = $"Assets/PREFAB/{name}.prefab";
        
        // Ensure directory exists
        if (!AssetDatabase.IsValidFolder("Assets/PREFAB"))
        {
            AssetDatabase.CreateFolder("Assets", "PREFAB");
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(bottle, path);
        Object.DestroyImmediate(bottle);
        
        return prefab;
    }

    private GameObject CreateSmokeParticles(string name, Color color)
    {
        GameObject particleObj = new GameObject(name);
        ParticleSystem ps = particleObj.AddComponent<ParticleSystem>();
        
        var main = ps.main;
        main.startLifetime = 2f;
        main.startSpeed = 0.5f;
        main.startSize = 0.1f;
        main.startColor = color;
        main.maxParticles = 100;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 20;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.05f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(color, 0.0f), new GradientColorKey(color, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        colorOverLifetime.color = gradient;

        ps.Stop();
        return particleObj;
    }

    private GameObject CreatePouringParticles()
    {
        GameObject particleObj = new GameObject("PouringParticles");
        ParticleSystem ps = particleObj.AddComponent<ParticleSystem>();
        
        var main = ps.main;
        main.startLifetime = 0.5f;
        main.startSpeed = 2f;
        main.startSize = 0.02f;
        main.startColor = new Color(0.4f, 0.3f, 0.1f); // Oil color
        main.maxParticles = 200;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1f;
        main.playOnAwake = false;

        var emission = ps.emission;
        emission.rateOverTime = 100;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 5f;
        shape.radius = 0.01f;
        shape.rotation = new Vector3(90, 0, 0);

        ps.Stop();
        return particleObj;
    }

    private void CreateUICanvas(AR_OilInteraction interaction)
    {
        // UI is now auto-created by AR_OilInteraction - no separate canvas needed
        Debug.Log("UI is auto-created by AR_OilInteraction component. No separate UI canvas needed.");
    }
}
