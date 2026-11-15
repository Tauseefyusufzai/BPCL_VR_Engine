===============================================
AR ENGINE OIL DEMO - SETUP GUIDE
===============================================

CREATED FILES:
--------------
1. AR_OilInteraction.cs - Main AR interaction script
2. AR_OilInteractionUI.cs - Auto UI setup helper
3. AR_OilDemoSetupWizard.cs - Editor setup wizard

QUICK SETUP (3 Steps):
----------------------
1. Open Unity Editor
2. Go to: Window > AR Oil Demo Setup
3. Click "FULL SETUP (All Components)"

FEATURES:
---------
✓ Auto plane detection and object spawning
✓ Touch-based bottle selection with highlighting
✓ Phone tilt gesture for pouring (accelerometer)
✓ Two oil types with different effects:

  MAK SUPREME SYNTHETIC (BPCL):
  • Smooth engine animation
  • No smoke particles
  • Green benefits panel with auto-scroll
  • Shows 4 key benefits

  MINERAL BASED (LOCAL):
  • Vibrating engine animation
  • Black smoke particles
  • Red demerits panel with auto-scroll
  • Shows 4 key demerits

✓ Reset button to restart demo

REQUIREMENTS:
-------------
- Unity 2021.3+ (or compatible version)
- AR Foundation package installed
- ARCore XR Plugin (for Android)
- TextMeshPro package
- Target: Android (Samsung S23)
- Build Settings: Android, API Level 24+

PREFABS NEEDED:
---------------
1. Engine_Prefab - Your 3D engine model
2. BPCL_Bottle - MAK Supreme Synthetic bottle
3. Local_Bottle - Mineral oil bottle

(The wizard can create sample bottles if none provided)

SCENE HIERARCHY (Auto-created):
-------------------------------
AR Session
AR Session Origin
  └─ AR Camera
  └─ AR Plane Manager
  └─ AR Raycast Manager
AR_OilInteraction_Manager
  └─ Particle Systems (smoke, pour effects)
AR_OilDemo_UI (Canvas)

CONTROLS:
---------
• Tap on bottle = Select oil type
• Tilt phone forward = Pour oil (hold ~2 seconds)
• Tap Reset button = Restart demo

TILT THRESHOLD:
---------------
Input.acceleration.y < -0.3
(Phone tilted ~30 degrees forward)

BUILD SETTINGS:
---------------
1. File > Build Settings
2. Switch Platform: Android
3. Player Settings:
   - Minimum API Level: Android 7.0 (API 24)
   - Graphics API: OpenGL ES3 or Vulkan
   - Package Name: com.yourcompany.aroildemo
   - ARM64 Architecture
4. XR Plug-in Management:
   - Enable ARCore
5. Build and Run

TESTING IN EDITOR:
------------------
- Use mouse clicks instead of touch
- Press 'T' key to simulate phone tilt (when bottle selected)
- AR features won't work in editor (plane detection disabled)

TROUBLESHOOTING:
----------------
Q: Nothing spawns?
A: Ensure horizontal surface is detected. Move phone to scan floor/table.

Q: Touch not working?
A: Check Camera tag is "MainCamera" and Physics Raycaster is active.

Q: Tilt not detected?
A: Build to device - accelerometer doesn't work in Unity Editor.

Q: UI not showing?
A: Check AR_OilInteractionUI component on Canvas and links to AR_OilInteraction.

Q: Bottles too big/small?
A: Adjust "objectScale" in AR_OilInteraction inspector (default 0.1).

CUSTOMIZATION:
--------------
Inspector Variables:
- spawnDistance: Distance from camera (default 1.0m)
- bottleSpacing: Space between bottles (default 0.3m)
- tiltThreshold: Tilt sensitivity (default -0.3)
- pourDuration: Time to complete pour (default 2.0s)
- vibrationIntensity: Engine shake amount (default 0.005)

CONTACT INFO:
-------------
Benefits Text: MAK Supreme Synthetic Lube
Warning Text: Mineral Based Lube

For more information, visit:
BPCL - Energizing Lives
MAK Lubricants

===============================================
Version: 1.0
Created: 2026-03-30
Platform: Samsung S23 (Android)
===============================================
