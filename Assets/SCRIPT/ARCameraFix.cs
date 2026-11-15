using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Fixes AR Camera issues - ensures camera feed is visible and tracking works.
/// Add this to the AR Camera GameObject.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ARCameraFix : MonoBehaviour
{
    private Camera cam;
    private ARCameraManager cameraManager;
    private ARCameraBackground cameraBackground;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        cameraManager = GetComponent<ARCameraManager>();
        cameraBackground = GetComponent<ARCameraBackground>();

        // Critical camera settings for AR
        FixCameraSettings();
    }

    private void Start()
    {
        Debug.Log("=== AR CAMERA FIX ===");
        Debug.Log($"Camera: {cam.name}");
        Debug.Log($"ARCameraManager present: {cameraManager != null}");
        Debug.Log($"ARCameraBackground present: {cameraBackground != null}");
        
        if (cameraManager == null)
        {
            Debug.LogWarning("Adding ARCameraManager to camera!");
            cameraManager = gameObject.AddComponent<ARCameraManager>();
        }
        
        if (cameraBackground == null)
        {
            Debug.LogWarning("Adding ARCameraBackground to camera!");
            cameraBackground = gameObject.AddComponent<ARCameraBackground>();
        }

        // Ensure camera background is enabled
        if (cameraBackground != null)
        {
            cameraBackground.enabled = true;
        }

        // Log XR session state
        Debug.Log($"AR Session State: {ARSession.state}");
        
        // Check culling mask
        int defaultLayer = LayerMask.NameToLayer("Default");
        bool defaultLayerVisible = (cam.cullingMask & (1 << defaultLayer)) != 0;
        Debug.Log($"Default layer in culling mask: {defaultLayerVisible}");
        
        if (!defaultLayerVisible)
        {
            Debug.LogWarning("Default layer NOT in culling mask! Objects won't be visible!");
            cam.cullingMask |= (1 << defaultLayer);
            Debug.Log("Added Default layer to culling mask");
        }
    }

    private void FixCameraSettings()
    {
        // Clear flags should be Depth for AR (only render 3D objects, not skybox)
        // But ARCameraBackground handles this
        
        // IMPORTANT: Don't use Solid Color - it will cover the camera feed!
        // ARCameraBackground replaces the clear with camera feed
        
        // Ensure proper clipping planes for AR
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        
        // Ensure camera renders ALL layers including Default
        // The Default layer (0) must be included or 3D objects won't render
        cam.cullingMask = -1; // All layers
        
        // Set camera tag
        cam.tag = "MainCamera";
    }

    private void OnEnable()
    {
        ARSession.stateChanged += OnARSessionStateChanged;
    }

    private void OnDisable()
    {
        ARSession.stateChanged -= OnARSessionStateChanged;
    }

    private void OnARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        Debug.Log($"AR Session State Changed: {args.state}");
        
        if (args.state == ARSessionState.SessionTracking)
        {
            Debug.Log("AR Tracking Active!");
        }
        else if (args.state == ARSessionState.Unsupported)
        {
            Debug.LogError("AR is not supported on this device!");
        }
    }

    private void Update()
    {
        // Debug info every 5 seconds
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"[ARCameraFix] Camera Position: {transform.position}, Session State: {ARSession.state}");
        }
    }
}
