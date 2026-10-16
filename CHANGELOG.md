# Changelog

## 0.1.2 — 2026-10-16

- Exclude Canvas UI from capture batches and restore its enabled state before drawing the viewer.
- Suppress standalone Light2D volumetric geometry during captures while preserving normal sprite illumination and the viewer's light volumes.
- Reset the helper camera to Game type after CopyFrom so Scene View geometry and volume settings are not inherited.
- Explicitly disable helper post-processing/XR/HDR display output and match the viewer's shadow setting.
- Defer helper/resource destruction when a render callback disables the component, preventing destruction of a camera that is still rendering.
- Restore the active render target when a capture request fails, and recover normal scene rendering for the next camera.
- Separate temporary capture state from camera coordination; centralize camera setup and remove duplicate initialization.
- Check UI pixels, volumetric pixels, illumination, final camera post-processing and state restoration in both renderers and repeated Play Mode cycles.

## 0.1.1 — 2026-10-16

- Exclude Scene View gizmos from layer captures so camera outlines and custom gizmo fills are not blurred or color graded.
- Restore the Scene View gizmo toggle after the capture batch, including when rendering fails.
- Add a regression check for capture isolation and normal editor gizmo visibility in Universal and 2D Renderer.
- Resolve the demo sample using the installed package version in the validation script.

## 0.1.0 — 2026-10-16

- Per-group Gaussian blur, exposure, tint, saturation and contrast for orthographic URP scenery.
- Scene View preview, SpriteMask-aware capture, quality settings and neutral bypass.
- Runtime and Editor assemblies, Undo-aware Inspector and bulk component assignment.
- Batched capture isolation, two reusable image buffers and cached Gaussian weights.
- Importable demo, rendering checks, repeated Play Mode checks and Windows build verification.
- MIT license, by marmunc.
