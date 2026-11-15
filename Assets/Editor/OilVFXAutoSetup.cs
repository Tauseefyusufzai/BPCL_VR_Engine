using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public class OilVFXAutoSetup : EditorWindow
{
    [MenuItem("Tools/AR Oil Setup/1. Automate VFX Dependencies (Run First)")]
    public static void RunSetup()
    {
        Debug.Log("[OilVFXAutoSetup] Starting Automation...");

        // 1. Create VFX Directories
        CreateFolder("Assets", "VFX");
        CreateFolder("Assets/VFX", "Materials");

        // 2. Create the URP Transparent Material
        string matPath = "Assets/VFX/Materials/OilDroplet_URP.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit != null)
            {
                mat = new Material(urpLit);
                
                // Configure URP Lit for Transparent/Alpha
                mat.SetFloat("_Surface", 1);        // Transparent
                mat.SetFloat("_Blend", 0);          // Alpha
                mat.SetFloat("_ZWrite", 0);         // Off
                mat.SetFloat("_Cull", 0);           // Both sides
                mat.SetFloat("_Smoothness", 0.82f); // Shiny oil
                mat.SetFloat("_Metallic", 0.1f);
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;             // Transparent queue

                AssetDatabase.CreateAsset(mat, matPath);
                Debug.Log($"[OilVFXAutoSetup] Created Material: {matPath}");
            }
            else
            {
                Debug.LogWarning("[OilVFXAutoSetup] Could not find URP Lit shader.");
            }
        }

        // 3. Add NozzlePoint to Bottle Prefabs
        AddNozzleToPrefab("Assets/PREFAB/BPCL_Bottle.prefab");
        AddNozzleToPrefab("Assets/PREFAB/Local_Bottle.prefab");

        // 4. Try to setup the Manager in the open scene
        GameObject manager = GameObject.Find("AR_OilInteraction_Manager");
        if (manager != null)
        {
            OilPourVFXController vfxController = manager.GetComponent<OilPourVFXController>();
            if (vfxController == null)
            {
                vfxController = manager.AddComponent<OilPourVFXController>();
            }

            AR_OilInteraction interaction = manager.GetComponent<AR_OilInteraction>();
            if (interaction != null && interaction.oilVFXController == null)
            {
                interaction.oilVFXController = vfxController;
                EditorUtility.SetDirty(interaction);
            }
            Debug.Log("[OilVFXAutoSetup] Wired AR_OilInteraction_Manager in the scene.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("VFX Setup - Step 1 Complete", 
            "The script has processed automation where possible:\n\n" +
            "✓ Created OilDroplet_URP.mat\n" +
            "✓ Added NozzlePoint to Bottle Prefabs\n" +
            "✓ Wired Controller in Scene\n\n" +
            "Please read GitHub Copilot's message to do the visual node setup manually (VFX Graph + SDF Bake).", "Understood");
    }

    private static void CreateFolder(string parent, string newFolder)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{newFolder}"))
        {
            AssetDatabase.CreateFolder(parent, newFolder);
        }
    }

    private static void AddNozzleToPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance.transform.Find("NozzlePoint") == null)
            {
                GameObject nozzle = new GameObject("NozzlePoint");
                nozzle.transform.SetParent(instance.transform);
                // Move it slightly above the visual center
                nozzle.transform.localPosition = new Vector3(0, 0.15f, 0); 
                PrefabUtility.SaveAsPrefabAsset(instance, path);
                Debug.Log($"[OilVFXAutoSetup] Added NozzlePoint to {path}");
            }
            DestroyImmediate(instance);
        }
        else
        {
            Debug.LogWarning($"[OilVFXAutoSetup] Could not find prefab at {path}");
        }
    }
}