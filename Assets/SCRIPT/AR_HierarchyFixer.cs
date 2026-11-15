using UnityEngine;
using UnityEngine.XR.ARFoundation;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// AR Oil Demo - Scene Hierarchy Fixer
/// This script ensures correct scene hierarchy for AR to work properly.
/// 
/// REQUIRED HIERARCHY (in this order):
/// 
/// 1. AR Session (GameObject)
///    - ARSession component
///    - ARInputManager component
/// 
/// 2. XR Origin (GameObject)
///    - XROrigin component
///    - ARPlaneManager component
///    - ARRaycastManager component
///    └── Camera Offset (child)
///        └── AR Camera (child, tagged MainCamera)
///            - Camera component
///            - ARCameraManager component
///            - ARCameraBackground component
///            - TrackedPoseDriver (Input System) component
/// 
/// 3. AR_OilInteraction_Manager (GameObject - NOT under Canvas!)
///    - AR_OilInteraction component
/// 
/// 4. UI_Canvas (GameObject - SEPARATE from AR_OilInteraction_Manager!)
///    - Canvas (ScreenSpaceOverlay)
///    - AR_OilInteractionUI component
/// 
/// COMMON MISTAKE: Putting AR_OilInteraction_Manager UNDER the Canvas!
/// If you do this, spawned objects become UI elements, not 3D AR objects!
/// </summary>
public class AR_HierarchyFixer : MonoBehaviour
{
    [Header("Click Fix All to automatically setup AR scene")]
    [SerializeField] private bool showDebugGUI = true;
    
    private string lastMessage = "";
    private float messageTime = 0;
    
    private void Start()
    {
        // Auto-check on start
        CheckAndLogHierarchy();
    }
    
    public void CheckAndLogHierarchy()
    {
        Debug.Log("==========================================================");
        Debug.Log("=== AR OIL DEMO - SCENE HIERARCHY CHECK ===");
        Debug.Log("==========================================================");
        
        bool allGood = true;
        
        // 1. Check AR Session
        ARSession arSession = FindFirstObjectByType<ARSession>();
        if (arSession != null)
        {
            Debug.Log($"✓ AR Session found: {arSession.gameObject.name}");
        }
        else
        {
            Debug.LogError("✗ AR Session NOT FOUND!");
            allGood = false;
        }
        
        // 2. Check XR Origin
        var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            Debug.Log($"✓ XR Origin found: {xrOrigin.gameObject.name}");
            
            // Check for AR managers
            ARPlaneManager planeManager = xrOrigin.GetComponent<ARPlaneManager>();
            ARRaycastManager raycastManager = xrOrigin.GetComponent<ARRaycastManager>();
            
            if (planeManager != null)
                Debug.Log($"  ✓ ARPlaneManager on XR Origin");
            else
                Debug.LogWarning($"  ✗ ARPlaneManager NOT on XR Origin!");
                
            if (raycastManager != null)
                Debug.Log($"  ✓ ARRaycastManager on XR Origin");
            else
                Debug.LogWarning($"  ✗ ARRaycastManager NOT on XR Origin!");
        }
        else
        {
            Debug.LogError("✗ XR Origin NOT FOUND!");
            allGood = false;
        }
        
        // 3. Check AR Camera
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Debug.Log($"✓ Main Camera found: {mainCamera.gameObject.name}");
            
            // Check components
            if (mainCamera.GetComponent<ARCameraManager>() != null)
                Debug.Log("  ✓ ARCameraManager present");
            else
            {
                Debug.LogError("  ✗ ARCameraManager MISSING! Camera won't show real world!");
                allGood = false;
            }
                
            if (mainCamera.GetComponent<ARCameraBackground>() != null)
                Debug.Log("  ✓ ARCameraBackground present");
            else
            {
                Debug.LogError("  ✗ ARCameraBackground MISSING! Camera background will be black!");
                allGood = false;
            }
            
            var trackedPoseDriver = mainCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (trackedPoseDriver != null)
                Debug.Log("  ✓ TrackedPoseDriver present");
            else
                Debug.LogWarning("  ⚠ TrackedPoseDriver MISSING! Camera won't track device movement!");
        }
        else
        {
            Debug.LogError("✗ Main Camera NOT FOUND!");
            allGood = false;
        }
        
        // 4. Check AR_OilInteraction
        AR_OilInteraction oilInteraction = FindFirstObjectByType<AR_OilInteraction>();
        if (oilInteraction != null)
        {
            Debug.Log($"✓ AR_OilInteraction found: {oilInteraction.gameObject.name}");
            
            // CRITICAL CHECK: Is it under a Canvas?
            Canvas parentCanvas = oilInteraction.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                Debug.LogError("  ✗ ✗ ✗ CRITICAL ERROR ✗ ✗ ✗");
                Debug.LogError($"  AR_OilInteraction is UNDER a Canvas ({parentCanvas.gameObject.name})!");
                Debug.LogError("  This makes ALL spawned objects into UI elements!");
                Debug.LogError("  FIX: Move AR_OilInteraction_Manager OUT of the Canvas hierarchy!");
                allGood = false;
            }
            else
            {
                Debug.Log("  ✓ AR_OilInteraction is NOT under Canvas (correct!)");
            }
            
            // Show parent hierarchy
            Transform current = oilInteraction.transform;
            string hierarchy = current.name;
            while (current.parent != null)
            {
                current = current.parent;
                hierarchy = current.name + " > " + hierarchy;
            }
            Debug.Log($"  Hierarchy: {hierarchy}");
        }
        else
        {
            Debug.LogError("✗ AR_OilInteraction NOT FOUND!");
            allGood = false;
        }
        
        // 5. UI is now auto-created by AR_OilInteraction - no separate UI component needed
        Debug.Log("ℹ UI is auto-created by AR_OilInteraction (no separate UI component needed)");
        
        Debug.Log("==========================================================");
        if (allGood)
        {
            Debug.Log("=== ALL CHECKS PASSED! AR should work correctly. ===");
            lastMessage = "All checks passed!";
        }
        else
        {
            Debug.LogError("=== PROBLEMS FOUND! See errors above. ===");
            lastMessage = "Problems found - check console!";
        }
        Debug.Log("==========================================================");
        
        messageTime = Time.time;
    }
    
#if UNITY_EDITOR
    [ContextMenu("Check Hierarchy")]
    public void EditorCheckHierarchy()
    {
        CheckAndLogHierarchy();
    }
    
    [ContextMenu("Auto-Fix Hierarchy")]
    public void AutoFixHierarchy()
    {
        Debug.Log("=== AUTO-FIXING AR HIERARCHY ===");
        
        // Find AR_OilInteraction
        AR_OilInteraction oilInteraction = FindFirstObjectByType<AR_OilInteraction>();
        if (oilInteraction != null)
        {
            // Check if under Canvas
            Canvas parentCanvas = oilInteraction.GetComponentInParent<Canvas>();
            if (parentCanvas != null)
            {
                Debug.Log($"Moving {oilInteraction.gameObject.name} out of Canvas...");
                
                // Move to scene root
                oilInteraction.transform.SetParent(null);
                oilInteraction.transform.position = Vector3.zero;
                oilInteraction.transform.rotation = Quaternion.identity;
                
                Debug.Log($"Moved {oilInteraction.gameObject.name} to scene root!");
                
                // Mark scene dirty for saving
                UnityEditor.EditorUtility.SetDirty(oilInteraction.gameObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(oilInteraction.gameObject.scene);
            }
        }
        
        // Add missing components to camera
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            if (mainCam.GetComponent<ARCameraManager>() == null)
            {
                mainCam.gameObject.AddComponent<ARCameraManager>();
                Debug.Log("Added ARCameraManager to camera");
            }
            
            if (mainCam.GetComponent<ARCameraBackground>() == null)
            {
                mainCam.gameObject.AddComponent<ARCameraBackground>();
                Debug.Log("Added ARCameraBackground to camera");
            }
            
            UnityEditor.EditorUtility.SetDirty(mainCam.gameObject);
        }
        
        Debug.Log("=== AUTO-FIX COMPLETE ===");
        
        // Re-check
        CheckAndLogHierarchy();
    }
#endif

    private void OnGUI()
    {
        if (!showDebugGUI) return;
        
        // Show check button
        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 24;
        
        if (GUI.Button(new Rect(10, 10, 200, 60), "Check AR Setup", buttonStyle))
        {
            CheckAndLogHierarchy();
        }
        
        // Show last message
        if (Time.time - messageTime < 5f && !string.IsNullOrEmpty(lastMessage))
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 20;
            labelStyle.normal.textColor = Color.yellow;
            GUI.Label(new Rect(10, 80, 400, 40), lastMessage, labelStyle);
        }
    }
}
