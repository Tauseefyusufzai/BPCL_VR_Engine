using UnityEngine;
using UnityEditor;

/// <summary>
/// Custom editor for AdvancedEngineAnimator with quick setup tools
/// </summary>
[CustomEditor(typeof(AdvancedEngineAnimator))]
public class AdvancedEngineAnimatorEditor : Editor
{
    private bool showQuickSetup = true;
    private bool showRuntimeInfo = true;

    public override void OnInspectorGUI()
    {
        AdvancedEngineAnimator animator = (AdvancedEngineAnimator)target;

        // Quick Setup Section
        showQuickSetup = EditorGUILayout.Foldout(showQuickSetup, "Quick Setup Tools", true, EditorStyles.foldoutHeader);
        if (showQuickSetup)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Auto-Find Components", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These tools help you automatically assign components based on naming conventions.\n" +
                "Crankshaft: Look for 'crankshaft', 'crank'\n" +
                "Pistons: Look for 'piston'\n" +
                "Connecting Rods: Look for 'rod', 'conrod'",
                MessageType.Info);

            if (GUILayout.Button("Auto-Find Crankshaft"))
            {
                AutoFindCrankshaft(animator);
            }

            if (GUILayout.Button("Auto-Find All Cylinders"))
            {
                AutoFindCylinders(animator);
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Test Controls", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Rev Up"))
            {
                animator.SetThrottle(1f);
            }
            if (GUILayout.Button("Idle"))
            {
                animator.SetThrottle(0f);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to test controls", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        // Runtime Info Section (only in Play Mode)
        if (Application.isPlaying)
        {
            showRuntimeInfo = EditorGUILayout.Foldout(showRuntimeInfo, "Runtime Info", true, EditorStyles.foldoutHeader);
            if (showRuntimeInfo)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField($"Current RPM: {animator.GetCurrentRPM():F0}");
                EditorGUILayout.LabelField($"Throttle: {animator.GetThrottle() * 100:F0}%");
                EditorGUILayout.LabelField($"Crank Angle: {animator.GetCrankAngle():F1}°");

                // RPM progress bar
                Rect rpmRect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rpmRect, animator.GetNormalizedRPM(),
                    animator.IsAtRedline() ? "REDLINE!" : "RPM");

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();

                // Force continuous repaint
                Repaint();
            }
        }

        // Draw default inspector
        DrawDefaultInspector();
    }

    private void AutoFindCrankshaft(AdvancedEngineAnimator animator)
    {
        SerializedProperty crankProp = serializedObject.FindProperty("crankshaft");

        Transform root = animator.transform;
        Transform found = FindChildByKeywords(root, new[] { "crankshaft", "crank", "Crankshaft", "Crank" });

        if (found != null)
        {
            crankProp.objectReferenceValue = found;
            serializedObject.ApplyModifiedProperties();
            Debug.Log($"Found crankshaft: {found.name}");
        }
        else
        {
            Debug.LogWarning("Could not find crankshaft. Make sure it contains 'crankshaft' or 'crank' in the name.");
        }
    }

    private void AutoFindCylinders(AdvancedEngineAnimator animator)
    {
        Transform root = animator.transform;

        // Find all pistons
        var pistons = FindAllChildrenByKeywords(root, new[] { "piston", "Piston" });
        var rods = FindAllChildrenByKeywords(root, new[] { "rod", "Rod", "conrod", "Conrod", "connecting" });

        if (pistons.Count == 0)
        {
            Debug.LogWarning("Could not find any pistons. Make sure they contain 'piston' in the name.");
            return;
        }

        SerializedProperty cylindersProp = serializedObject.FindProperty("cylinders");
        cylindersProp.arraySize = pistons.Count;

        for (int i = 0; i < pistons.Count; i++)
        {
            SerializedProperty cylinderProp = cylindersProp.GetArrayElementAtIndex(i);
            SerializedProperty pistonProp = cylinderProp.FindPropertyRelative("piston");
            SerializedProperty rodProp = cylinderProp.FindPropertyRelative("connectingRod");

            pistonProp.objectReferenceValue = pistons[i];

            // Try to find matching connecting rod
            string pistonNumber = ExtractNumber(pistons[i].name);
            Transform matchingRod = null;

            foreach (var rod in rods)
            {
                string rodNumber = ExtractNumber(rod.name);
                if (!string.IsNullOrEmpty(pistonNumber) && pistonNumber == rodNumber)
                {
                    matchingRod = rod;
                    break;
                }
            }

            // If no number match, try to match by parent or proximity
            if (matchingRod == null && i < rods.Count)
            {
                matchingRod = rods[i];
            }

            if (matchingRod != null)
            {
                rodProp.objectReferenceValue = matchingRod;
            }
        }

        serializedObject.ApplyModifiedProperties();
        Debug.Log($"Found {pistons.Count} pistons and {rods.Count} connecting rods");
    }

    private Transform FindChildByKeywords(Transform root, string[] keywords)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>())
        {
            foreach (string keyword in keywords)
            {
                if (child.name.ToLower().Contains(keyword.ToLower()))
                {
                    return child;
                }
            }
        }
        return null;
    }

    private System.Collections.Generic.List<Transform> FindAllChildrenByKeywords(Transform root, string[] keywords)
    {
        var results = new System.Collections.Generic.List<Transform>();

        foreach (Transform child in root.GetComponentsInChildren<Transform>())
        {
            foreach (string keyword in keywords)
            {
                if (child.name.ToLower().Contains(keyword.ToLower()))
                {
                    results.Add(child);
                    break;
                }
            }
        }

        // Sort by name to maintain consistent ordering
        results.Sort((a, b) => a.name.CompareTo(b.name));
        return results;
    }

    private string ExtractNumber(string text)
    {
        string number = "";
        foreach (char c in text)
        {
            if (char.IsDigit(c))
            {
                number += c;
            }
        }
        return number;
    }
}
