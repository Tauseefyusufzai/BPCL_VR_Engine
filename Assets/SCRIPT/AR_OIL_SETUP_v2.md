# AR Oil Demo v2 - Setup Guide

## IMPORTANT: Complete Rewrite

The AR_OilInteraction script has been completely rewritten with:

✅ **Clean State Machine Architecture**
- `WaitingForPlane` → `Idle` → `Holding` → `Pouring` → `PourComplete`
- Clear state transitions with enter/exit logic

✅ **Fixed Tilt Detection**
- **Old threshold was -0.35** (too sensitive, triggered accidentally)
- **New threshold: 0.65** (absolute value, requires deliberate tilt)
- Phone upright ≈ 0, Phone tilted forward ≈ -0.7 to -1.0

✅ **Proper Selection/Deselection**
- Tap bottle = select (you are now "holding" it)
- Tap same bottle again = deselect
- Tap different bottle = switch

✅ **Distance Check**
- If you move > 2.5m from engine while pouring = "Wrong Pouring!" alert

✅ **No Reset Button**
- To try another oil, simply tap a different bottle
- Engine state auto-resets when selecting new bottle

---

## Setup Instructions

### 1. Scene Setup

1. Create empty GameObject at scene ROOT (not under Canvas!)
2. Name it: `AR_OilInteraction_Manager`
3. Add component: `AR_OilInteraction`

### 2. Assign References

In Inspector:

**PREFABS (Required):**
- `Engine Prefab` - Your engine 3D model
- `BPCL Bottle Prefab` - MAK/BPCL oil bottle
- `Local Bottle Prefab` - Generic/mineral oil bottle

**AR References:**
- `AR Plane Manager` - From XR Origin
- `AR Raycast Manager` - From XR Origin  
- `AR Camera` - Main Camera / AR Camera

### 3. Optional Settings

**Spawn Settings:**
- `Spawn Distance` - How far in front of camera (default: 1.5m)
- `Object Scale` - Scale multiplier (default: 1.5)
- `Bottle Spacing` - Distance between bottles (default: 0.4m)

**Tilt Detection:**
- `Tilt Start Threshold` - How much tilt to START pouring (0.5-0.9, default: 0.65)
- `Tilt Continue Threshold` - How much tilt to CONTINUE (0.4-0.8, default: 0.55)
- `Pour Duration` - Seconds to complete pour (default: 3.0)

**Distance Check:**
- `Max Pour Distance` - Maximum allowed distance from engine (default: 2.5m)

### 4. Optional: Particle Systems

Assign these if you have them:
- `Pouring Particles` - Oil stream effect
- `Smoke Particles` - Black smoke for bad oil

### 5. Optional: Audio

Assign audio clips:
- `Select Sound`
- `Deselect Sound`
- `Pouring Sound`
- `Success Sound`
- `Warning Sound`

---

## How It Works

### User Flow:

1. **Launch App** → Scans for horizontal surface
2. **Surface Found** → Engine + 2 bottles spawn in AR
3. **Tap Bottle** → Bottle highlights (you're "holding" it)
4. **Tilt Phone Forward** → Pouring begins
5. **Keep Tilting** → Progress bar fills
6. **Pour Complete** → Benefits/Demerits panel appears
7. **Tap Other Bottle** → Engine resets, try again

### State Machine:

```
Initializing
    ↓
WaitingForPlane (scanning)
    ↓
Spawning (creating objects)
    ↓
Idle (waiting for tap)
    ↓ tap bottle
Holding (bottle selected)
    ↓ tilt phone
Pouring (filling progress)
    ↓ complete / ↓ fail (too far)
PourComplete ←——————→ PourFailed
    ↓ tap different bottle
[Reset & Idle]
```

---

## Troubleshooting

### "Objects don't appear"
- Make sure AR_OilInteraction_Manager is at SCENE ROOT, NOT under Canvas
- Check if prefabs are assigned

### "Touch doesn't work"
- Ensure bottles have colliders (script auto-adds if missing)
- Check that bottle prefabs are assigned

### "Tilt doesn't work"
- Check logcat for "Accelerometer enabled" message
- Try adjusting `Tilt Start Threshold` (lower = less tilt needed)

### "Pour starts immediately"
- Increase `Tilt Start Threshold` to 0.7 or 0.8
- This means phone was already tilted when you tapped

---

## Key Improvements from v1

| Issue | v1 | v2 |
|-------|----|----|
| Tilt threshold | -0.35 (too sensitive) | 0.65 absolute (requires real tilt) |
| Auto-pour on tap | Yes (bug) | No, requires actual tilt |
| Tap to deselect | No | Yes |
| Bottle switching | Required reset | Auto-resets engine |
| Distance check | None | 2.5m limit |
| State management | Flags | Clean state machine |
| Code architecture | Spaghetti | SOLID, clean |

---

## Debug Logging

The script logs state changes and tilt values. Look for:
```
[AR_Oil_v2] State: Idle → Holding
[AR_Oil_v2] State=Holding, Selected=BPCL, Tilt=0.423, SmoothedTilt=0.312, Pour=0.00
```

---

Created: 2026-03-30
Version: 2.0.0
