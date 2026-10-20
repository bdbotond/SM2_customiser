# Space Marine 2 Customizer & 3D Armouring Studio (`SM2Customizer`)

A standalone desktop application, 3D armouring studio, and mod manager for **Warhammer 40,000: Space Marine 2** (PC).

`SM2Customizer` features a split-screen interactive **3D Viewport** and customization suite that allows you to inspect and customize your Space Marine models in real time—swapping armor parts, adjusting Citadel paint colors, testing metallic finishes, applying chapter decals, and importing custom 3D meshes (OBJ, STL, GLTF, GLB) with relative sizing and micro-transform controls.

---

## Key Features

- **Interactive 3D Viewport (WebGL PBR)**:
  - 360-degree orbit camera, pan, zoom, and studio lighting with soft ground shadows.
  - **Click-to-Select Parts in 3D**: Clicking directly on an armor piece in the 3D viewport (Helmet, Pauldron, Chest, Greave, Shield) automatically selects that slot in the customization controls.
  - **Live Real-Time PBR Recoloring**: Adjusting color sliders, metallic toggles, and specular highlights immediately updates the 3D marine in real time.
  - **Quick Camera Focus Buttons**: Instant camera presets for `[Full Body]`, `[Helmet]`, `[Left Shoulder]`, `[Right Shoulder]`, `[Chest]`, `[Backpack]`, `[Arms]`, `[Legs]`, and `[Reset View]`.
  - **Forge Lighting & Isolation Modes**: Inspect individual armor components with solo isolate mode, wireframe toggle, and turntable auto-rotation.
- **Dynamic Game Folder Discovery**: Completely decoupled from game installations. Point the app to your Space Marine 2 folder, and it dynamically navigates game archive structures to load armor parts, colors, decals, and rules from your installed game version.
- **Multi-Format 3D Model Importer (OBJ, STL, GLTF, GLB)**:
  - Import custom community 3D armor parts and weapon accessories in **Wavefront OBJ**, **Stereolithography STL** (binary and ASCII), and **GLTF 2.0 / GLB** formats.
  - **Automatic Relative Sizing**: Automatically computes bounding boxes and normalizes scales to canonical Astartes anatomical armor dimensions.
  - **3D Micro-Transform Toolbar**: Real-time in-viewport sliders for fine-tuning Scale (0.2x–3.0x), Offset X/Y/Z, Yaw rotation (-180° to +180°), slot reassignment, and reset controls.
- **Cross-Class Armor Equipping**: Equip Bulwark shields, Sniper cloaks/hoods, Heavy iron halos, and Vanguard antennas on any class, and equip Chaos marine armor on Loyalist marines.
- **Advanced Color & Material Shaders**:
  - Full Citadel paint palette (Macragge Blue, Leadbelcher, Mephiston Red, Retributor Gold, White Scar, etc.).
  - Custom RGBA sliders with live hex input.
  - Metallic finish (`isMetallic`) toggles and specular highlight tint adjustments.
- **Preset Management**: Save and load custom marine loadouts as `.json` files to share with friends.

---

## Installation & Running

### Option 1: Standalone Executable (.exe)
Run `SM2Customizer.exe` located at:
```text
dist\SM2Customizer.exe
```
The program is self-contained and runs on Windows 10/11 (64-bit).

### Option 2: Build From Source

Prerequisites:
- Windows 10 / 11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# 1. Run automated repository validation & sanity checks
powershell -ExecutionPolicy Bypass -File .\check.ps1

# 2. Build the standalone single-file executable
powershell -ExecutionPolicy Bypass -File .\build_exe.ps1
```
The output executable will be created at `dist\SM2Customizer.exe`.

---

## Repository Verification (`check.ps1`)

Before pushing or submitting pull requests, run:
```powershell
powershell -ExecutionPolicy Bypass -File .\check.ps1
```
The check script verifies:
1. .NET 8 SDK availability and compiler warnings/errors (`-warnaserror`).
2. HTML/JS web asset integrity (all scripts referenced in `viewer.html` exist on disk).
3. Offline unit & service sanity checks.
4. Security & hygiene audit (no leaked secrets, no files > 25MB, no un-ignored game assets).

---

## Planned Features & Engineering Roadmap

- [ ] **Deploy Mod (.pak) & Restore Vanilla Engine**: Temporarily moved out of the primary UI ribbon for workflow refactoring. Will be restored with an advanced packaging pipeline once asset painting pipelines are finalized.
- [ ] **Custom Image Upload & Surface Painting**: Upload arbitrary PNG/JPEG textures and stamp or wrap them onto specific armor parts.
- [ ] **In-Engine Decal Rendering on 3D Mesh**: Real-time rendering of authentic chapter emblems and markings directly on curved shoulder pads and greaves.
- [ ] **Preset Export with Custom Vertex Paint Layers**: Persist custom painted vertex colors into JSON presets and export to game textures.

---
