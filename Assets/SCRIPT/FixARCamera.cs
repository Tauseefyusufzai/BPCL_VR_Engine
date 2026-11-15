using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Ensures AR Camera is properly configured for AR rendering
/// Attach this to AR Camera or run once in scene
/// </summary>
public class FixARCamera : MonoBehaviour
{
    private void Start()
    {
        FixCameraSettings();
    }

    [ContextMenu("Fix AR Camera Now")]
    public void FixCameraSettings()
    {
        // Find AR Camera
        Camera arCamera = Camera.main;
        if (arCamera == null)
        {
            Debug.LogError("No main camera found!");
            return;
        }

        // Get or add AR Camera Background
        ARCameraBackground cameraBackground = arCamera.GetComponent<ARCameraBackground>();
        if (cameraBackground == null)
        {
            cameraBackground = arCamera.gameObject.AddComponent<ARCameraBackground>();
            Debug.Log("Added ARCameraBackground component");
        }

        // Get or add AR Camera Manager
        ARCameraManager cameraManager = arCamera.GetComponent<ARCameraManager>();
        if (cameraManager == null)
        {
            cameraManager = arCamera.gameObject.AddComponent<ARCameraManager>();
            Debug.Log("Added ARCameraManager component");
        }

        // Fix camera settings
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        arCamera.backgroundColor = Color.black;
        arCamera.nearClipPlane = 0.1f;
        arCamera.farClipPlane = 1000f; // Increased for better visibility
        arCamera.depth = 1; // Ensure it renders on top

        // Ensure camera renders everything
        arCamera.cullingMask = -1; // Render all layers

        Debug.Log($"<color=green>✓ AR Camera Fixed!</color>");
        Debug.Log($"  Clear Flags: {arCamera.clearFlags}");
        Debug.Log($"  Culling Mask: {arCamera.cullingMask}");
        Debug.Log($"  AR Background: {(cameraBackground != null ? "✓" : "✗")}");
        Debug.Log($"  AR Manager: {(cameraManager != null ? "✓" : "✗")}");
    }

    private void OnValidate()
    {
        // Auto-fix when component is added
        if (Application.isPlaying)
        {
            FixCameraSettings();
        }
    }
}
