using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;

/// <summary>
/// Complete AR Setup Validator and Fixer
/// Add this to any GameObject in your scene - it will fix AR issues automatically
/// </summary>
public class AR_SetupValidator : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool autoFixOnStart = true;

    private void Start()
    {
        if (autoFixOnStart)
        {
            StartCoroutine(ValidateAndFixAR());
        }
    }

    [ContextMenu("Validate and Fix AR Setup")]
    public void RunValidation()
    {
        StartCoroutine(ValidateAndFixAR());
    }

    private IEnumerator ValidateAndFixAR()
    {
        Log("=== AR SETUP VALIDATION ===");
        yield return new WaitForSeconds(0.5f);

        // Step 1: Check AR Session
        ARSession arSession = FindFirstObjectByType<ARSession>();
        if (arSession == null)
        {
            LogError("AR Session NOT FOUND! Creating one...");
            GameObject sessionObj = new GameObject("AR Session");
            arSession = sessionObj.AddComponent<ARSession>();
            sessionObj.AddComponent<ARInputManager>();
        }
        else
        {
            Log("✓ AR Session found");
        }

        // Step 2: Check XR Origin (newer) or AR Session Origin (older)
        var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        ARSessionOrigin arSessionOrigin = FindFirstObjectByType<ARSessionOrigin>();
        
        GameObject originObject = null;
        
        if (xrOrigin != null)
        {
            Log("✓ XR Origin found");
            originObject = xrOrigin.gameObject;
        }
        else if (arSessionOrigin != null)
        {
            Log("✓ AR Session Origin found");
            originObject = arSessionOrigin.gameObject;
        }
        else
        {
            LogError("NO XR Origin or AR Session Origin found!");
        }

        // Step 3: Check AR Camera
        Camera arCamera = Camera.main;
        if (arCamera == null)
        {
            LogError("Main Camera NOT FOUND!");
            yield break;
        }
        
        Log($"✓ Main Camera found: {arCamera.name}");
        Log($"  Position: {arCamera.transform.position}");
        Log($"  Parent: {(arCamera.transform.parent != null ? arCamera.transform.parent.name : "NONE")}");

        // Step 4: Check AR Camera Manager
        ARCameraManager cameraManager = arCamera.GetComponent<ARCameraManager>();
        if (cameraManager == null)
        {
            LogError("ARCameraManager NOT on camera! Adding...");
            cameraManager = arCamera.gameObject.AddComponent<ARCameraManager>();
        }
        else
        {
            Log("✓ ARCameraManager found");
        }

        // Step 5: Check AR Camera Background
        ARCameraBackground cameraBackground = arCamera.GetComponent<ARCameraBackground>();
        if (cameraBackground == null)
        {
            LogError("ARCameraBackground NOT on camera! Adding...");
            cameraBackground = arCamera.gameObject.AddComponent<ARCameraBackground>();
        }
        else
        {
            Log("✓ ARCameraBackground found");
        }

        // Step 6: Check Tracked Pose Driver
        var trackedPoseDriver = arCamera.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        if (trackedPoseDriver == null)
        {
            LogWarning("TrackedPoseDriver NOT on camera - camera won't track device movement!");
            // Try to add it
            try
            {
                var tpd = arCamera.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                Log("✓ Added TrackedPoseDriver");
            }
            catch
            {
                LogWarning("Could not add TrackedPoseDriver - make sure Input System package is installed");
            }
        }
        else
        {
            Log("✓ TrackedPoseDriver found");
        }

        // Step 7: Check AR Plane Manager
        ARPlaneManager planeManager = FindFirstObjectByType<ARPlaneManager>();
        if (planeManager == null)
        {
            LogError("ARPlaneManager NOT FOUND!");
            if (originObject != null)
            {
                planeManager = originObject.AddComponent<ARPlaneManager>();
                Log("✓ Added ARPlaneManager to origin");
            }
        }
        else
        {
            Log($"✓ ARPlaneManager found on: {planeManager.gameObject.name}");
        }

        // Step 8: Check AR Raycast Manager
        ARRaycastManager raycastManager = FindFirstObjectByType<ARRaycastManager>();
        if (raycastManager == null)
        {
            LogError("ARRaycastManager NOT FOUND!");
            if (originObject != null)
            {
                raycastManager = originObject.AddComponent<ARRaycastManager>();
                Log("✓ Added ARRaycastManager to origin");
            }
        }
        else
        {
            Log($"✓ ARRaycastManager found on: {raycastManager.gameObject.name}");
        }

        // Step 9: Check camera settings
        Log($"Camera Clear Flags: {arCamera.clearFlags}");
        Log($"Camera Culling Mask: {arCamera.cullingMask}");
        Log($"Camera Near Clip: {arCamera.nearClipPlane}");
        Log($"Camera Far Clip: {arCamera.farClipPlane}");

        // Step 10: Check for Canvas issues
        AR_OilInteraction oilInteraction = FindFirstObjectByType<AR_OilInteraction>();
        if (oilInteraction != null)
        {
            Transform parent = oilInteraction.transform.parent;
            if (parent != null)
            {
                Canvas parentCanvas = parent.GetComponent<Canvas>();
                if (parentCanvas != null || parent.GetComponentInParent<Canvas>() != null)
                {
                    LogError("!!! AR_OilInteraction is UNDER A CANVAS! This is WRONG!");
                    LogError("!!! Move AR_OilInteraction_Manager OUT of the Canvas!");
                }
                else
                {
                    Log($"AR_OilInteraction parent: {parent.name}");
                }
            }
            else
            {
                Log("✓ AR_OilInteraction is at root level (correct!)");
            }
        }

        Log("=== VALIDATION COMPLETE ===");
        
        yield return new WaitForSeconds(1f);
        
        // Log AR Session state
        if (arSession != null)
        {
            Log($"AR Session State: {ARSession.state}");
        }
    }

    private void Log(string message)
    {
        if (showDebugLogs)
            Debug.Log($"[AR_Validator] {message}");
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning($"[AR_Validator] {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"[AR_Validator] {message}");
    }
}
