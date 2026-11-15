using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EngineSimulation
{
    /// <summary>
    /// Configuration utility to set up the Oil Bubble Particle System perfectly for Unity 6000.3.5f1.
    /// </summary>
    [ExecuteAlways]
    public class OilParticleSystemSetup : MonoBehaviour
    {
        /// <summary>
        /// Context menu to automatically apply the exact settings requested for the oil bubble simulation.
        /// </summary>
        [ContextMenu("Setup Oil Bubble Particle System")]
        public void SetupParticleSystem()
        {
            ParticleSystem ps = GetComponent<ParticleSystem>();
            if (ps == null)
            {
                ps = gameObject.AddComponent<ParticleSystem>();
            }

            // --- MAIN MODULE ---
            var main = ps.main;
            main.duration = 4.0f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);
            
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.30f, 0.04f, 0.75f),
                new Color(0.75f, 0.50f, 0.10f, 0.60f)
            );
            
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.15f); // Increased negative gravity for more lift
            main.maxParticles = 10000; // Massive particle count
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = false;

            // --- EMISSION MODULE ---
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(2500f); // High emission to quickly fill the chamber

            // --- SHAPE MODULE ---
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.15f, 0.05f, 0.15f);

            // --- VELOCITY OVER LIFETIME MODULE ---
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.35f); // Increased upward drift to simulate fast rising oil
            vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);

            // --- SIZE OVER LIFETIME MODULE ---
            var sizeLT = ps.sizeOverLifetime;
            sizeLT.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(new Keyframe(0f, 0.3f));
            sizeCurve.AddKey(new Keyframe(0.3f, 1.0f));
            sizeCurve.AddKey(new Keyframe(0.85f, 0.9f));
            sizeCurve.AddKey(new Keyframe(1f, 0f));
            sizeLT.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // --- COLOR OVER LIFETIME MODULE ---
            var colorLT = ps.colorOverLifetime;
            colorLT.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] 
                {
                    new GradientColorKey(new Color(0.6f, 0.35f, 0.05f), 0.0f),
                    new GradientColorKey(new Color(0.8f, 0.55f, 0.15f), 1.0f)
                },
                new GradientAlphaKey[] 
                {
                    new GradientAlphaKey(0.0f, 0.0f),
                    new GradientAlphaKey(200f / 255f, 0.2f),
                    new GradientAlphaKey(200f / 255f, 0.8f),
                    new GradientAlphaKey(0.0f, 1.0f)
                }
            );
            colorLT.color = new ParticleSystem.MinMaxGradient(gradient);

            // --- NOISE MODULE ---
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.05f);
            noise.frequency = 0.8f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.2f);
            // Damping maps to remap capability in Noisemodule depending on the Unity definition, mapping to true as best fit
            noise.damping = true;

            // --- RENDERER MODULE ---
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 1;
            
            if (renderer.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Particles/Standard Unlit");
                if (shader != null)
                {
                    renderer.sharedMaterial = new Material(shader);
                }
            }

            Debug.Log("[OilSetup] Particle system configured for Unity 6 — assign a bubble sprite material in the Renderer for best results.");

#if UNITY_EDITOR
            EditorUtility.SetDirty(ps.gameObject);
#endif
        }

        private void OnValidate()
        {
            var renderer = GetComponent<ParticleSystemRenderer>();
            if (renderer != null && renderer.sharedMaterial == null)
            {
                Debug.LogWarning("[OilSetup] No material assigned to ParticleSystemRenderer. Please assign a valid Bubble/Oil material to ensure proper rendering.", this);
            }
        }
    }
}
