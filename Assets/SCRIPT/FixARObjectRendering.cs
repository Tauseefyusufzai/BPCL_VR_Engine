using UnityEngine;

/// <summary>
/// Ensures spawned AR objects have proper materials and are visible
/// Fixes common rendering issues in AR
/// </summary>
public class FixARObjectRendering : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool fixOnStart = true;
    [SerializeField] private bool makeObjectsOpaque = true;
    [SerializeField] private bool fixLighting = true;

    private void Start()
    {
        if (fixOnStart)
        {
            Invoke(nameof(FixAllObjects), 3f); // Wait 3 seconds for objects to spawn
        }
    }

    [ContextMenu("Fix All AR Objects")]
    public void FixAllObjects()
    {
        Debug.Log("=== Fixing AR Object Rendering ===");

        // Find all renderers in scene
        Renderer[] allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        
        int fixedCount = 0;
        foreach (Renderer renderer in allRenderers)
        {
            // Skip UI and camera objects
            if (renderer.gameObject.layer == LayerMask.NameToLayer("UI")) continue;
            if (renderer.GetComponent<Camera>() != null) continue;

            // Fix materials
            foreach (Material mat in renderer.materials)
            {
                if (mat == null) continue;

                if (makeObjectsOpaque)
                {
                    // Ensure materials are opaque
                    Color col = mat.color;
                    col.a = 1f;
                    mat.color = col;

                    // Set render mode to opaque
                    mat.SetFloat("_Mode", 0); // 0 = Opaque
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 2000;
                }

                if (fixLighting)
                {
                    // Enable proper lighting
                    mat.EnableKeyword("_NORMALMAP");
                    mat.SetFloat("_Smoothness", 0.5f);
                    mat.SetFloat("_Metallic", 0.0f);
                }

                fixedCount++;
            }

            // Ensure renderer is enabled
            renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        Debug.Log($"<color=green>✓ Fixed {fixedCount} materials on {allRenderers.Length} renderers</color>");
    }

    [ContextMenu("List All Scene Objects")]
    public void ListAllObjects()
    {
        Debug.Log("=== Scene Objects ===");
        
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.InstanceID);
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.ToLower().Contains("engine") || 
                obj.name.ToLower().Contains("bottle") ||
                obj.name.ToLower().Contains("bpcl") ||
                obj.name.ToLower().Contains("local"))
            {
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
                Debug.Log($"  {obj.name}: Position={obj.transform.position}, Active={obj.activeInHierarchy}, Renderers={renderers.Length}");
            }
        }
    }
}
