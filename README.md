# BPCL VR Engine — Interactive VR/AR Lubricant Oil Simulation

An interactive **Virtual Reality (VR) & Augmented Reality (AR) Engine Lubrication Simulation** built with **Unity 6 (6000.3.5f1)** and **Universal Render Pipeline (URP)**. Designed for **BPCL (Bharat Petroleum)** lubricant demonstrations, this project lets users grab and pour different engine oils (**BPCL Premium**, **BPCL Supreme**, and **Local/Standard Oil**) into a procedurally animated internal combustion engine and observe real-time mechanical, visual, thermal, and audio feedback.

---

## Key Features

### 1. VR & AR Interactive Oil Pouring (`VR_OilInteraction`, `AR_OilInteraction_v2`)
- **XR Grab & Auto-Pour**: Grab oil bottles (`BPCL_Premium`, `BPCL_Supreme`, `Local`) using OpenXR / XR Interaction Toolkit controllers or hand tracking. Bringing a bottle within range of the engine's oil filler neck automatically aligns, tilts, and pours oil into the engine.
- **Pour Scoring & Telemetry**: Tracks pour duration, stability score, control score, and tilt drops during manual/assisted pouring.
- **Two-Phase Oil Flow (`OilPourManager`)**:
  - **Phase 1 — Pour Stream (`OilParticleConfigurator`, `OilPourVFXController`)**: Viscosity-specific particle stream from the bottle nozzle into the filler hole.
  - **Phase 2 — Shader Tank Fill (`OilFillController`)**: Smooth shader-driven oil level rise inside the sump/tank after pouring completes.

### 2. Real-Time Engine Comparison & Visual Feedback
- **Transparent Engine Cutaway**: Fades the outer engine block materials to reveal internal moving pistons, connecting rods, and crankshaft after pouring.
- **Oil Quality Visuals & Audio**:
  - **BPCL Premium / Supreme Oil**: Golden engine glow transition, golden piston lubrication particles (`PistonOilParticleController`), smooth high-RPM operation, and voiceover/UI highlighting BPCL lubricant benefits.
  - **Local / Substandard Oil**: Darkened/blackened engine shader response, overheating smoke/degradation particles, rough engine loop audio, and warnings on wear and tear.
- **World-Space VR UI & RPM Gauge (`EngineRPMGauge`)**: Dynamic world-space canvas displaying live engine status, lubricant comparison metrics, benefits panels, and start/reset controls.

### 3. Procedural Engine Kinematics (`AdvancedEngineAnimator`)
- **Math-Driven Kinematics**: Animates crankshaft, connecting rods, and pistons using exact crank-slider kinematics without baked animations.
- **Multi-Configuration Support**: Supports `Inline4`, `Inline6`, `V6`, `V8`, `V12`, `Boxer4`, `Boxer6`, `Single`, and `Custom` cylinder bank/phase setups.
- **Dynamic Throttle, RPM & Vibration**: Smooth RPM acceleration/deceleration, pitch-modulated engine audio, and procedural engine block vibration.

---

## Tech Stack & Packages

| Component | Version / Details |
| :--- | :--- |
| **Engine** | Unity `6000.3.5f1` (Unity 6) |
| **Render Pipeline** | Universal Render Pipeline (`com.unity.render-pipelines.universal` `17.3.0`) |
| **XR Management & OpenXR** | `com.unity.xr.management` `4.5.3`, `com.unity.xr.openxr` `1.16.1`, `com.unity.xr.androidxr-openxr` `1.1.0` |
| **XR Interaction & Hands** | `com.unity.xr.interaction.toolkit` `3.3.1`, `com.unity.xr.hands` `1.7.2` |
| **AR Support** | `com.unity.xr.arfoundation` `6.3.2` |
| **Input & Camera** | Unity Input System `1.17.0`, Cinemachine `3.1.6` |

---

## Project Structure

```text
VR_OIL_SIMULATION/
├── Assets/
│   ├── EngineV8/                        # 3D V8 engine meshes, pistons, crankshaft & materials
│   ├── PREFAB/                          # Garage environment & pre-configured engine/bottle prefabs
│   ├── Scenes/
│   │   ├── VR Oil Engine.unity          # Primary VR showroom & interactive oil simulation scene
│   │   ├── VR Oil Engine Normal.unity   # Standard comparison scene variant
│   │   └── BasicScene.unity             # Lightweight testing scene
│   ├── SCRIPT/
│   │   ├── VR_OilInteraction.cs         # Core VR grab, auto-pour, cutaway, audio & UI controller
│   │   ├── AR_OilInteraction_v2.cs      # AR touch/placement oil interaction state machine
│   │   ├── AdvancedEngineAnimator.cs    # Procedural piston, rod & crankshaft kinematics
│   │   ├── OilPourManager.cs            # Orchestrates pour stream & shader tank fill phases
│   │   ├── OilParticleConfigurator.cs   # Configures particle properties per oil type
│   │   ├── OilFillController.cs         # Controls shader fill level in the engine sump
│   │   ├── PistonOilParticleController.cs # Internal piston lubrication VFX controller
│   │   ├── EngineRPMGauge.cs            # Live RPM needle/UI readout
│   │   └── Editor/                      # Custom Unity Editor setup wizards & inspectors
│   ├── Shaders/                         # Custom URP shaders for oil fill and cutaway rendering
│   ├── SOUND/                           # Voiceovers, pour SFX, alarms & engine RPM audio loops
│   └── XR/ & XRI/                       # OpenXR & XR Interaction Toolkit settings and presets
├── Packages/                            # Unity Package Manager manifest & lockfile
└── ProjectSettings/                     # Project, URP, Input, and OpenXR configuration
```

---

## Getting Started

### Prerequisites
1. **Unity Hub** with **Unity `6000.3.5f1`** installed (include Android Build Support + OpenXR modules if deploying to standalone VR headsets such as Meta Quest / Android XR).
2. **Git** and **Git LFS** installed (required for 100 MB+ 3D models, HDRI skyboxes, and baked lightmaps).

### Clone the Repository
```bash
git lfs install
git clone https://github.com/Tauseefyusufzai/BPCL_VR_Engine.git
cd BPCL_VR_Engine
git lfs pull
```

### Open & Run in Unity
1. Open **Unity Hub**, click **Add -> Add project from disk**, and select the cloned `BPCL_VR_Engine` folder.
2. Once Unity finishes importing packages and compiling scripts, open `Assets/Scenes/VR Oil Engine.unity`.
3. **Testing in Editor**:
   - Press **Play** to use the **XR Device Simulator** (or a connected PCVR headset via Link/SteamVR/OpenXR).
   - Grab any of the oil bottles (**BPCL Premium**, **BPCL Supreme**, or **Local Oil**) and move it near the engine filler cap to trigger the pour sequence.
4. **Editor Setup Wizards**:
   - Custom setup utilities are available under `Assets/SCRIPT/Editor/` (`EngineSetupWizard.cs`, `AR_OilDemoSetupWizard.cs`, and custom inspector controls on `AdvancedEngineAnimator`).

---

## Building for VR Headset

1. Go to **File -> Build Profiles** (or **Build Settings**).
2. Select **Android** (for standalone headsets like Meta Quest / Android XR) or **Windows, Mac, Linux** (for PCVR).
3. Ensure `Assets/Scenes/VR Oil Engine.unity` is checked in the Scene List.
4. Verify OpenXR interaction profiles under **Edit -> Project Settings -> XR Plug-in Management -> OpenXR**.
5. Click **Build** or **Build and Run**.
