/*
 * SETUP STEPS (Unity 6000.3.5f1):
 * 1. Inside the engine prefab, find the piston container GameObject.
 * 2. Add a child GameObject named "OilBubbleParticles".
 * 3. Attach a ParticleSystem component to "OilBubbleParticles".
 * 4. Attach OilParticleSystemSetup.cs to "OilBubbleParticles".
 * 5. Right-click OilParticleSystemSetup in the Inspector → 
 *    "Setup Oil Bubble Particle System" to auto-configure all settings.
 * 6. Attach PistonOilParticleController.cs to the piston container GameObject.
 * 7. In the Inspector, assign:
 *      - oilAnimator       → the Animator on the engine prefab root
 *      - oilBubbleParticleSystem → the ParticleSystem on "OilBubbleParticles"
 *      - oilPourAnimationStateName → exact name of the oil pour animation state
 * 8. Set "OilBubbleParticles" GameObject to INACTIVE in the hierarchy by default.
 * 9. (Optional) In the oil pour Animation Clip, add an Animation Event on the 
 *    last frame pointing to PistonOilParticleController.ActivateOilBubbles()
 *    for more precise timing instead of the coroutine approach.
 *
 * TESTED ON: Unity 6000.3.5f1
 */

using System.Collections;
using UnityEngine;

namespace EngineSimulation
{
    /// <summary>
    /// Controls the timing and activation of the oil bubble particle system based on an animation state.
    /// </summary>
    public class PistonOilParticleController : MonoBehaviour
    {
        [SerializeField] private Animator oilAnimator;
        [SerializeField] private ParticleSystem oilBubbleParticleSystem;
        [SerializeField] private float activationDelaySeconds = 0.2f;

        private void EnsureInitialized()
        {
            if (oilBubbleParticleSystem != null) return;

            ParticleSystem[] childrenPS = GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in childrenPS)
            {
                if (ps.gameObject.CompareTag("OilBubble") || ps.gameObject.name.Contains("OilBubble"))
                {
                    oilBubbleParticleSystem = ps;
                    Debug.Log($"[OilSystem] Found ParticleSystem on child '{ps.gameObject.name}'");
                    break;
                }
            }

            if (oilBubbleParticleSystem == null)
            {
                Debug.LogWarning("[OilSystem] Particle System not assigned in inspector AND no child found with tag 'OilBubble' or name containing 'OilBubble'!");
            }
        }

        private void Start()
        {
            EnsureInitialized();

            if (oilBubbleParticleSystem != null)
            {
                oilBubbleParticleSystem.gameObject.SetActive(true); // Must be active in hierarchy for coroutines/components to tick
                oilBubbleParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        /// <summary>
        /// Activates the associated oil bubble particle system. 
        /// Triggered precisely by the BPCL pouring phase from AR_OilInteraction.cs
        /// </summary>
        public void ActivateOilBubbles()
        {
            EnsureInitialized();

            if (gameObject.activeInHierarchy)
            {
                StartCoroutine(DelayedActivation());
            }
            else
            {
                // If inactive, force right away
                TriggerParticles();
            }
        }

        private IEnumerator DelayedActivation()
        {
            yield return new WaitForSeconds(activationDelaySeconds);
            TriggerParticles();
        }

        private void TriggerParticles()
        {
            if (oilBubbleParticleSystem != null)
            {
                oilBubbleParticleSystem.gameObject.SetActive(true);
                var emit = oilBubbleParticleSystem.emission;
                emit.enabled = true; // Force ON in case it was disabled manually
                oilBubbleParticleSystem.Play(true);
                Debug.Log("[OilSystem] Oil bubble particles activated successfully!");
            }
            else 
            {
                Debug.LogWarning("[OilSystem] Tried to activate bubbles but oilBubbleParticleSystem reference is null!");
            }
        }

        /// <summary>
        /// Deactivates and stops the associated oil bubble particle system entirely.
        /// </summary>
        public void DeactivateOilBubbles()
        {
            if (oilBubbleParticleSystem != null)
            {
                oilBubbleParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                oilBubbleParticleSystem.gameObject.SetActive(false);
            }
        }
    }
}
