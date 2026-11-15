using UnityEngine;
using TMPro;

/// <summary>
/// AR Debug Helper - Shows detailed debug info to diagnose visibility issues
/// Attach this to AR_OilInteraction_Manager
/// </summary>
public class AR_DebugHelper : MonoBehaviour
{
    [Header("Debug Display")]
    [SerializeField] private TextMeshProUGUI debugText;
    [SerializeField] private bool showDebugPanel = true;
    
    private AR_OilInteraction oilInteraction;
    private Canvas debugCanvas;
    private GameObject debugPanel;

    private void Start()
    {
        oilInteraction = GetComponent<AR_OilInteraction>();
        
        if (showDebugPanel && debugText == null)
        {
            CreateDebugUI();
        }
    }

    private void Update()
    {
        if (debugText != null)
        {
            UpdateDebugInfo();
        }
    }

    private void CreateDebugUI()
    {
        // Create debug canvas
        GameObject canvasObj = new GameObject("DebugCanvas");
        debugCanvas = canvasObj.AddComponent<Canvas>();
        debugCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        debugCanvas.sortingOrder = 999;
        
        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Create panel
        debugPanel = new GameObject("DebugPanel");
        debugPanel.transform.SetParent(debugCanvas.transform, false);
        
        RectTransform panelRect = debugPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0);
        panelRect.anchorMax = new Vector2(0.5f, 0.4f);
        panelRect.offsetMin = new Vector2(10, 10);
        panelRect.offsetMax = new Vector2(-10, -10);

        UnityEngine.UI.Image panelBg = debugPanel.AddComponent<UnityEngine.UI.Image>();
        panelBg.color = new Color(0, 0, 0, 0.8f);

        // Create text
        GameObject textObj = new GameObject("DebugText");
        textObj.transform.SetParent(debugPanel.transform, false);
        
        debugText = textObj.AddComponent<TextMeshProUGUI>();
        debugText.fontSize = 18;
        debugText.color = Color.yellow;
        debugText.alignment = TextAlignmentOptions.TopLeft;
        debugText.enableWordWrapping = true;
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);
    }

    private void UpdateDebugInfo()
    {
        if (oilInteraction == null) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>=== AR DEBUG INFO ===</b>");
        sb.AppendLine($"<color=cyan>State:</color> {oilInteraction.GetCurrentState()}");
        sb.AppendLine($"<color=cyan>Selected:</color> {oilInteraction.GetSelectedOil()}");
        sb.AppendLine($"<color=cyan>Has Spawned:</color> {oilInteraction.HasSpawned()}");
        
        // Get engine via public method
        GameObject engine = oilInteraction.GetEngine();

        sb.AppendLine();
        sb.AppendLine("<b>SPAWNED OBJECTS:</b>");
        sb.AppendLine($"Engine: {GetObjectInfo(engine)}");

        sb.AppendLine();
        sb.AppendLine("<b>CAMERA INFO:</b>");
        Camera cam = Camera.main;
        if (cam != null)
        {
            sb.AppendLine($"Pos: {cam.transform.position}");
            sb.AppendLine($"Fwd: {cam.transform.forward}");
        }

        debugText.text = sb.ToString();
    }

    private string GetObjectInfo(GameObject obj)
    {
        if (obj == null)
            return "<color=red>NULL</color>";
        
        if (!obj.activeInHierarchy)
            return $"<color=orange>INACTIVE</color> {obj.name}";

        string info = $"<color=green>✓</color> {obj.name}\n";
        info += $"  Pos: {obj.transform.position}\n";
        info += $"  Scale: {obj.transform.localScale}\n";
        
        // Check renderers
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        info += $"  Renderers: {renderers.Length}";
        
        if (renderers.Length > 0)
        {
            int visible = 0;
            foreach (var r in renderers)
            {
                if (r.enabled && r.gameObject.activeInHierarchy)
                    visible++;
            }
            info += $" ({visible} visible)";
        }

        return info;
    }

    /// <summary>
    /// Force make all spawned objects large and visible
    /// </summary>
    [ContextMenu("Force Make Objects Visible")]
    public void ForceMakeVisible()
    {
        var type = oilInteraction.GetType();
        var engineField = type.GetField("spawnedEngine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var bpclField = type.GetField("spawnedBPCLBottle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var localField = type.GetField("spawnedLocalBottle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        GameObject engine = engineField?.GetValue(oilInteraction) as GameObject;
        GameObject bpcl = bpclField?.GetValue(oilInteraction) as GameObject;
        GameObject local = localField?.GetValue(oilInteraction) as GameObject;

        if (engine != null)
        {
            engine.transform.localScale = Vector3.one * 0.3f; // Make bigger
            MakeRenderersVisible(engine);
            Debug.Log($"Engine now at: {engine.transform.position}, Scale: {engine.transform.localScale}");
        }

        if (bpcl != null)
        {
            bpcl.transform.localScale = Vector3.one * 0.2f;
            MakeRenderersVisible(bpcl);
            Debug.Log($"BPCL now at: {bpcl.transform.position}, Scale: {bpcl.transform.localScale}");
        }

        if (local != null)
        {
            local.transform.localScale = Vector3.one * 0.2f;
            MakeRenderersVisible(local);
            Debug.Log($"Local now at: {local.transform.position}, Scale: {local.transform.localScale}");
        }
    }

    private void MakeRenderersVisible(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.enabled = true;
            r.gameObject.SetActive(true);
            
            // Ensure materials are not transparent/invisible
            if (r.material != null)
            {
                Color col = r.material.color;
                col.a = 1f;
                r.material.color = col;
            }
        }
    }

    /// <summary>
    /// Toggle debug panel visibility
    /// </summary>
    public void ToggleDebugPanel()
    {
        showDebugPanel = !showDebugPanel;
        if (debugPanel != null)
        {
            debugPanel.SetActive(showDebugPanel);
        }
    }
}
