# Validation — 0.1.1

Tested on Windows with Unity 6000.2.7f2, URP 17.2.0, Linear color space and an NVIDIA GeForce RTX 4070 SUPER.

## Functional checks

- Independent Runtime and Editor assembly compilation; UPM discovery and shader loading from Resources.
- Universal Renderer and 2D Renderer: independent/combined blur and color grading, Full/Half/Quarter resolution, blur radii 48/96/256, exposure and black lightening.
- Component disable restores original rendering and releases capture textures; pre-hidden sources retain their state.
- Internal/external SpriteMask, both inside/outside interaction modes, camera culling and mixed source Unity Layers.
- Capture isolation for unrelated translucent artwork; neutral/offscreen groups allocate no capture buffer.
- Scene View gizmos remain visible in the viewing camera but are excluded from the effect capture; enabled and disabled gizmo toggle states are preserved in both renderers.
- Imported Basic Demo: three effect components, no missing scripts and retained asset GUIDs.
- Two Play Mode cycles with Domain Reload and Scene Reload disabled.
- Windows development build and built-player rendering: packaged shader available, effects change the image and source visibility is restored.

The optimized demo differs from the preceding implementation by at most 1/255 per RGB channel due to reordered Gaussian sums. This comparison covers the demo, not every possible scene.

## Indicative performance

960×540, Half resolution, blur radius 12, five lit sprites per effect group and 2,000 other enabled renderers outside the viewport. Medians of 30 samples after five warmups:

| Effect groups | Request before, ms | Request after, ms | Render/readback before, ms | Render/readback after, ms |
|---|---:|---:|---:|---:|
| 1 | 1.315 | 0.900 | 5.154 | 4.468 |
| 4 | 2.230 | 1.600 | 6.091 | 5.207 |
| 8 | 3.669 | 2.544 | 7.613 | 6.546 |

Request time is CPU-side rendering/submission and may include waits. Render/readback includes GPU synchronization; neither is isolated GPU timing. These are synthetic-scene measurements, not whole-game FPS claims. Small scenes show less benefit and run-to-run variation. The allocation-counter API returned zero in both versions and is not evidence of allocation-free rendering.

## Limits

Other Unity/URP versions, non-Windows platforms, Gamma grading, complex SpriteMask sorting ranges, unusual materials and prefab-stage preview require separate verification. Perspective, stereo/XR, camera stacking and nested effect groups are outside v0.1 support.

## Reproduce

On Windows, with Unity 6000.2.7f2 and Windows build support:

```powershell
./Tests~/Run.ps1 -UnityEditor '<path-to-Unity.exe>'
```

The script creates a disposable project and retains its logs, build, screenshots and JSON results. WorkDirectory selects its parent directory. ReuseProject accepts only a previous project containing the checker marker file. It does not save or replace the caller's scene.
