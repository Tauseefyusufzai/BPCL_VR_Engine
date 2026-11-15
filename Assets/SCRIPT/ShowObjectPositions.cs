using UnityEngine;
using TMPro;

/// <summary>
/// Shows object positions on screen to help debug visibility
/// </summary>
public class ShowObjectPositions : MonoBehaviour
{
    private TextMeshProUGUI debugText;
    private GameObject debugPanel;

    private void Start()
    {
        CreateDebugUI();
    }

    private void CreateDebugUI()
    {
        // Create canvas
        GameObject canvasObj = new GameObject("PositionDebugCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();

        // Create panel (left side)
        debugPanel = new GameObject("DebugPanel");
        debugPanel.transform.SetParent(canvas.transform, false);
        
        RectTransform panelRect = debugPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 0.5f);
        panelRect.anchorMax = new Vector2(0.4f, 1f);
        panelRect.offsetMin = new Vector2(10, -10);
        panelRect.offsetMax = new Vector2(-10, -200);

        UnityEngine.UI.Image panelBg = debugPanel.AddComponent<UnityEngine.UI.Image>();
        panelBg.color = new Color(0, 0, 0, 0.85f);

        // Create text
        GameObject textObj = new GameObject("DebugText");
        textObj.transform.SetParent(debugPanel.transform, false);
        
        debugText = textObj.AddComponent<TextMeshProUGUI>();
        debugText.fontSize = 20;
        debugText.color = Color.cyan;
        debugText.alignment = TextAlignmentOptions.TopLeft;
        debugText.enableWordWrapping = true;
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);
    }

    private void Update()
    {
        if (debugText == null) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("<b><color=yellow>=== POSITION DEBUG ===</color></b>");
        
        Camera cam = Camera.main;
        if (cam != null)
        {
            sb.AppendLine($"\n<b>Camera:</b>");
            sb.AppendLine($"Pos: {cam.transform.position}");
            sb.AppendLine($"Rot: {cam.transform.eulerAngles}");
        }

        // Find spawned objects
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        
        GameObject engine = null;
        GameObject bpcl = null;
        GameObject local = null;

        foreach (var obj in allObjects)
        {
            string name = obj.name.ToLower();
            if (name.Contains("engine") && !name.Contains("prefab") && !name.Contains("animator"))
            {
                engine = obj;
            }
            else if (name.Contains("bpcl"))
            {
                bpcl = obj;
            }
            else if (name.Contains("local") && name.Contains("bottle"))
            {
                local = obj;
            }
        }

        sb.AppendLine($"\n<b>Objects Found:</b>");
        sb.AppendLine($"Engine: {GetObjectStatus(engine, cam)}");
        sb.AppendLine($"BPCL: {GetObjectStatus(bpcl, cam)}");
        sb.AppendLine($"Local: {GetObjectStatus(local, cam)}");

        debugText.text = sb.ToString();
    }

    private string GetObjectStatus(GameObject obj, Camera cam)
    {
        if (obj == null)
            return "<color=red>NOT FOUND</color>";

        if (!obj.activeInHierarchy)
            return $"<color=orange>INACTIVE</color>";

        string status = $"<color=green>✓</color> {obj.name}\n";
        status += $"  Pos: {obj.transform.position}\n";
        status += $"  Scale: {obj.transform.localScale}\n";
        
        if (cam != null)
        {
            float dist = Vector3.Distance(cam.transform.position, obj.transform.position);
            status += $"  Dist from Cam: {dist:F2}m\n";
            
            // Check if in view
            Vector3 viewportPoint = cam.WorldToViewportPoint(obj.transform.position);
            bool inView = viewportPoint.z > 0 && viewportPoint.x > 0 && viewportPoint.x < 1 && viewportPoint.y > 0 && viewportPoint.y < 1;
            status += $"  In View: {(inView ? "<color=green>YES</color>" : "<color=red>NO</color>")}\n";
            status += $"  Viewport: {viewportPoint}";
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        int visible = 0;
        foreach (var r in renderers)
        {
            if (r.enabled && r.gameObject.activeInHierarchy)
                visible++;
        }
        status += $"\n  Renderers: {visible}/{renderers.Length} visible";

        return status;
    }
}
