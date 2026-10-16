# Changelog

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
