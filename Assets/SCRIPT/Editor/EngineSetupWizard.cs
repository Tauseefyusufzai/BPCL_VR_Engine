using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Editor wizard to automatically set up the AdvancedEngineAnimator on any engine model
/// Analyzes the hierarchy and identifies pistons, crankshaft, and connecting rods
/// </summary>
public class EngineSetupWizard : EditorWindow
{
    private GameObject targetEngine;
    private Vector2 scrollPosition;

    // Discovered components
    private Transform crankshaft;
    private List<Transform> pistons = new List<Transform>();
    private List<Transform> connectingRods = new List<Transform>();
    private List<Transform> allTransforms = new List<Transform>();

    // Settings
    private AdvancedEngineAnimator.EngineType engineType = AdvancedEngineAnimator.EngineType.Inline6;
    private float crankRadius = 0.05f;
    private float rodLength = 0.15f;

    // Keywords for auto-detection (Dutch + English + German + common 3D naming)
    private readonly string[] crankKeywords = { "crank", "kruk", "kurbelwelle", "krukas", "crankshaft", "shaft", "axle", "as", "main", "hoofd" };
    private readonly string[] pistonKeywords = { "piston", "zuiger", "kolben", "cylinder", "cilinder", "cyl", "plunger" };
    private readonly string[] rodKeywords = { "rod", "stang", "drijfstang", "drijstang", "pleuel", "conrod", "connecting", "link", "arm" };

    [MenuItem("Tools/Engine Setup Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<EngineSetupWizard>("Engine Setup Wizard");
        window.minSize = new Vector2(450, 600);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Engine Setup Wizard", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This wizard analyzes your 3D engine model and automatically sets up the AdvancedEngineAnimator component.\n\n" +
            "1. Drag your engine model (from scene or prefab) below\n" +
            "2. Click 'Analyze Hierarchy'\n" +
            "3. Review and adjust detected components\n" +
            "4. Click 'Setup Engine Animator'",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // Target selection
        EditorGUI.BeginChangeCheck();
        targetEngine = (GameObject)EditorGUILayout.ObjectField("Engine Model", targetEngine, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck() && targetEngine != null)
        {
            AnalyzeHierarchy();
        }

        if (targetEngine == null)
        {
            EditorGUILayout.HelpBox("Please assign an engine model to begin.", MessageType.Warning);
            return;
        }

        // Check if it's a prefab/asset (not in scene)
        if (!IsSceneObject(targetEngine))
        {
            EditorGUILayout.HelpBox(
                "This is a Prefab/FBX asset. When you click 'Setup Engine Animator', " +
                "it will be automatically instantiated in your scene first.",
                MessageType.Info);

            if (GUILayout.Button("Instantiate in Scene Now", GUILayout.Height(25)))
            {
                GameObject sceneInstance = InstantiateInScene(targetEngine);
                sceneInstance.transform.position = Vector3.zero;
                targetEngine = sceneInstance;
                AnalyzeHierarchy();
                Selection.activeGameObject = sceneInstance;
            }
        }

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Analyze Hierarchy", GUILayout.Height(30)))
        {
            AnalyzeHierarchy();
        }

        EditorGUILayout.Space(10);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // Crankshaft Section
        EditorGUILayout.LabelField("Crankshaft", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        crankshaft = (Transform)EditorGUILayout.ObjectField("Crankshaft", crankshaft, typeof(Transform), true);
        if (crankshaft == null)
        {
            EditorGUILayout.HelpBox("No crankshaft detected. Please assign manually.", MessageType.Warning);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);

        // Pistons Section
        EditorGUILayout.LabelField($"Pistons ({pistons.Count} found)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        for (int i = 0; i < pistons.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            pistons[i] = (Transform)EditorGUILayout.ObjectField($"Piston {i + 1}", pistons[i], typeof(Transform), true);
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                pistons.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("+ Add Piston"))
        {
            pistons.Add(null);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);

        // Connecting Rods Section
        EditorGUILayout.LabelField($"Connecting Rods ({connectingRods.Count} found)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        for (int i = 0; i < connectingRods.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            connectingRods[i] = (Transform)EditorGUILayout.ObjectField($"Rod {i + 1}", connectingRods[i], typeof(Transform), true);
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                connectingRods.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }
        if (GUILayout.Button("+ Add Connecting Rod"))
        {
            connectingRods.Add(null);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // Engine Settings
        EditorGUILayout.LabelField("Engine Configuration", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        engineType = (AdvancedEngineAnimator.EngineType)EditorGUILayout.EnumPopup("Engine Type", engineType);
        crankRadius = EditorGUILayout.FloatField("Crank Radius", crankRadius);
        rodLength = EditorGUILayout.FloatField("Connecting Rod Length", rodLength);

        if (GUILayout.Button("Auto-Calculate Dimensions"))
        {
            AutoCalculateDimensions();
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // All Transforms (for manual selection)
        EditorGUILayout.LabelField("All Child Transforms (for manual selection)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        if (allTransforms.Count > 0)
        {
            EditorGUILayout.LabelField($"Found {allTransforms.Count} transforms in hierarchy:");
            EditorGUILayout.Space(5);

            foreach (var t in allTransforms.Take(50))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(t.name, GUILayout.Width(200));

                if (GUILayout.Button("Crank", GUILayout.Width(50)))
                {
                    crankshaft = t;
                }
                if (GUILayout.Button("Piston", GUILayout.Width(50)))
                {
                    if (!pistons.Contains(t)) pistons.Add(t);
                }
                if (GUILayout.Button("Rod", GUILayout.Width(50)))
                {
                    if (!connectingRods.Contains(t)) connectingRods.Add(t);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (allTransforms.Count > 50)
            {
                EditorGUILayout.LabelField($"... and {allTransforms.Count - 50} more");
            }
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);

        // Setup Button
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("Setup Engine Animator", GUILayout.Height(40)))
        {
            SetupEngineAnimator();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(5);

        if (GUILayout.Button("Print Hierarchy to Console"))
        {
            PrintHierarchy();
        }
    }

    private void AnalyzeHierarchy()
    {
        if (targetEngine == null) return;

        pistons.Clear();
        connectingRods.Clear();
        crankshaft = null;
        allTransforms.Clear();

        // Get all transforms
        allTransforms = targetEngine.GetComponentsInChildren<Transform>().ToList();

        // Find crankshaft
        foreach (var t in allTransforms)
        {
            string nameLower = t.name.ToLower();
            foreach (string keyword in crankKeywords)
            {
                if (nameLower.Contains(keyword))
                {
                    crankshaft = t;
                    break;
                }
            }
            if (crankshaft != null) break;
        }

        // Find pistons
        foreach (var t in allTransforms)
        {
            string nameLower = t.name.ToLower();
            foreach (string keyword in pistonKeywords)
            {
                if (nameLower.Contains(keyword))
                {
                    pistons.Add(t);
                    break;
                }
            }
        }

        // Find connecting rods
        foreach (var t in allTransforms)
        {
            string nameLower = t.name.ToLower();
            foreach (string keyword in rodKeywords)
            {
                if (nameLower.Contains(keyword) && !pistons.Contains(t))
                {
                    connectingRods.Add(t);
                    break;
                }
            }
        }

        // Sort by name for consistent ordering
        pistons = pistons.OrderBy(p => p.name).ToList();
        connectingRods = connectingRods.OrderBy(r => r.name).ToList();

        // Auto-detect engine type based on piston count
        if (pistons.Count == 1) engineType = AdvancedEngineAnimator.EngineType.Single;
        else if (pistons.Count == 4) engineType = AdvancedEngineAnimator.EngineType.Inline4;
        else if (pistons.Count == 6) engineType = AdvancedEngineAnimator.EngineType.Inline6;
        else if (pistons.Count == 8) engineType = AdvancedEngineAnimator.EngineType.V8;
        else if (pistons.Count == 12) engineType = AdvancedEngineAnimator.EngineType.V12;

        Debug.Log($"[Engine Setup] ========== ANALYSIS RESULTS ==========");
        Debug.Log($"[Engine Setup] Model: {targetEngine.name}");
        Debug.Log($"[Engine Setup] Total transforms found: {allTransforms.Count}");
        Debug.Log($"[Engine Setup] Crankshaft: {crankshaft?.name ?? "NOT FOUND"}");
        Debug.Log($"[Engine Setup] Pistons found: {pistons.Count}");
        foreach (var p in pistons) Debug.Log($"[Engine Setup]   - {p.name}");
        Debug.Log($"[Engine Setup] Connecting Rods found: {connectingRods.Count}");
        foreach (var r in connectingRods) Debug.Log($"[Engine Setup]   - {r.name}");
        Debug.Log($"[Engine Setup] =====================================");

        if (pistons.Count == 0)
        {
            Debug.LogWarning("[Engine Setup] No pistons found! Check Console for all transform names.");
            Debug.Log("[Engine Setup] All transforms in model:");
            foreach (var t in allTransforms)
            {
                Debug.Log($"  -> {t.name}");
            }
        }
    }

    private void AutoCalculateDimensions()
    {
        if (pistons.Count < 2)
        {
            EditorUtility.DisplayDialog("Cannot Calculate",
                "Need at least 2 pistons to auto-calculate dimensions.", "OK");
            return;
        }

        // Estimate based on piston spacing
        float avgSpacing = 0f;
        for (int i = 1; i < pistons.Count; i++)
        {
            avgSpacing += Vector3.Distance(pistons[i].position, pistons[i - 1].position);
        }
        avgSpacing /= (pistons.Count - 1);

        // Crank radius is typically about 1/4 to 1/3 of cylinder spacing
        crankRadius = avgSpacing * 0.25f;
        rodLength = crankRadius * 3f;

        Debug.Log($"[Engine Setup] Auto-calculated: CrankRadius={crankRadius:F4}, RodLength={rodLength:F4}");
    }

    private bool IsSceneObject(GameObject obj)
    {
        // Check if the object is in the scene (has a valid scene) or is a prefab/asset
        return obj.scene.IsValid() && !string.IsNullOrEmpty(obj.scene.name);
    }

    private GameObject InstantiateInScene(GameObject prefab)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance == null)
        {
            // Fallback for non-prefab assets (like FBX)
            instance = Object.Instantiate(prefab);
            instance.name = prefab.name; // Remove "(Clone)" suffix
        }
        Undo.RegisterCreatedObjectUndo(instance, "Instantiate Engine Model");
        return instance;
    }

    private void SetupEngineAnimator()
    {
        if (targetEngine == null)
        {
            EditorUtility.DisplayDialog("Error", "No engine model assigned.", "OK");
            return;
        }

        if (pistons.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "No pistons assigned.", "OK");
            return;
        }

        // Check if target is a scene object or a prefab/asset
        if (!IsSceneObject(targetEngine))
        {
            bool shouldInstantiate = EditorUtility.DisplayDialog(
                "Prefab/Asset Detected",
                "The selected model is a prefab or asset file (like FBX).\n\n" +
                "Components cannot be added to prefab assets directly. " +
                "Would you like to instantiate it in the scene first?\n\n" +
                "Click 'Yes' to create a scene instance and set it up.",
                "Yes, Instantiate",
                "Cancel");

            if (!shouldInstantiate) return;

            // Instantiate in scene
            GameObject sceneInstance = InstantiateInScene(targetEngine);
            sceneInstance.transform.position = Vector3.zero;
            sceneInstance.transform.rotation = Quaternion.identity;

            // Update target and re-analyze to get the new transforms
            targetEngine = sceneInstance;
            AnalyzeHierarchy();

            Debug.Log($"[Engine Setup] Created scene instance: {sceneInstance.name}");
        }

        Undo.RecordObject(targetEngine, "Setup Engine Animator");

        // Add or get the component
        var animator = targetEngine.GetComponent<AdvancedEngineAnimator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<AdvancedEngineAnimator>(targetEngine);
        }

        // Use SerializedObject to set private fields
        SerializedObject so = new SerializedObject(animator);

        so.FindProperty("engineType").enumValueIndex = (int)engineType;
        so.FindProperty("crankshaft").objectReferenceValue = crankshaft;
        so.FindProperty("crankRadius").floatValue = crankRadius;
        so.FindProperty("connectingRodLength").floatValue = rodLength;

        // Setup cylinders array
        SerializedProperty cylindersProp = so.FindProperty("cylinders");
        cylindersProp.arraySize = pistons.Count;

        for (int i = 0; i < pistons.Count; i++)
        {
            SerializedProperty cylinder = cylindersProp.GetArrayElementAtIndex(i);
            cylinder.FindPropertyRelative("piston").objectReferenceValue = pistons[i];

            if (i < connectingRods.Count)
            {
                cylinder.FindPropertyRelative("connectingRod").objectReferenceValue = connectingRods[i];
            }
        }

        so.ApplyModifiedProperties();

        EditorUtility.DisplayDialog("Success",
            $"Engine Animator setup complete!\n\n" +
            $"Engine Type: {engineType}\n" +
            $"Pistons: {pistons.Count}\n" +
            $"Connecting Rods: {connectingRods.Count}\n\n" +
            "Enter Play Mode to test the animation.",
            "OK");

        Selection.activeGameObject = targetEngine;
    }

    private void PrintHierarchy()
    {
        if (targetEngine == null) return;

        Debug.Log("=== ENGINE HIERARCHY ===");
        PrintTransformRecursive(targetEngine.transform, 0);
        Debug.Log("========================");
    }

    private void PrintTransformRecursive(Transform t, int depth)
    {
        string indent = new string(' ', depth * 2);
        Debug.Log($"{indent}{t.name}");

        foreach (Transform child in t)
        {
            PrintTransformRecursive(child, depth + 1);
        }
    }

    private void OnSelectionChange()
    {
        if (Selection.activeGameObject != null)
        {
            targetEngine = Selection.activeGameObject;
            AnalyzeHierarchy();
            Repaint();
        }
    }
}
