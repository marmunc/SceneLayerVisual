# Validation — 0.1.2

Version `0.1.2` passed regression checks for Canvas exclusion, standalone Light2D volume exclusion, original sprite lighting, camera type/shadow settings and restoration of enabled/disabled inputs. These checks run in Edit Mode and both repeated Play Mode cycles. Headless checks use the Scene View drawing context and real camera render requests; actual Scene View captures were inspected separately in the development project.

Tested on Windows with Unity 6000.2.7f2, URP 17.2.0, Linear color space and an NVIDIA GeForce RTX 4070 SUPER.

## Functional checks

- Independent Runtime and Editor assembly compilation; UPM discovery and shader loading from Resources.
- Universal Renderer and 2D Renderer: independent/combined blur and color grading, Full/Half/Quarter resolution, blur radii 48/96/256, exposure and black lightening.
- Component disable restores original rendering and releases capture textures; pre-hidden sources retain their state. Render callbacks that disable/re-enable or destroy a component are covered separately.
- Internal/external SpriteMask, both inside/outside interaction modes, camera culling and mixed source Unity Layers.
- Capture isolation for unrelated translucent artwork; neutral/offscreen groups allocate no capture buffer.
- Scene View gizmos remain visible in the viewing camera but are excluded from the effect capture; enabled and disabled gizmo toggle states are preserved in both renderers.
- World-space and camera-space UI and standalone Light2D volume pixels are absent from the capture and remain present in the viewing camera. Normal lighting of captured sprites matches the unprocessed reference in 2D Renderer.
- Game and Scene View capture helpers use Game camera type, disable post-processing/volumes and preserve the viewer's shadow setting. Enabled and pre-disabled Canvas/Light2D states are restored.
- The viewing camera's final Volume exposure is applied once: an unrelated pixel changes from 0.051 to 0.103 with exposure +1, with the same result while layer capture is active. This uses a StandardRequest so URP updates the Volume framework.
- An intentionally thrown render callback exception cannot leave the injected render target, hidden scene inputs or disabled gizmos in the viewing camera. The next capture runs successfully. The expected validation exception is identified separately from unexpected errors.
- Imported Basic Demo: three effect components, no missing scripts and retained asset GUIDs.
- Two Play Mode cycles with Domain Reload and Scene Reload disabled.
- Windows development build and built-player rendering: packaged shader available, effects change the image and source visibility is restored.

In an existing URP 2D development scene, before/after raw captures confirmed exclusion of a camera-space vignette and a volumetric Sprite Light2D beam in Scene View and a Game-type camera render. Component settings and prefab references were preserved. Scene View and gameplay were also manually checked by the package author. Automated repeated Play Mode checks use the disposable package project and do not validate the entire host game.

The optimized demo differs from the preceding implementation by at most 1/255 per RGB channel due to reordered Gaussian sums. This comparison covers the demo, not every possible scene.

## Historical optimization measurements (0.1.0)

These measurements predate the UI/light-volume capture-isolation changes. Version 0.1.2 adds Canvas/Light2D scans and temporary Canvas enable changes; its CPU/UI rebuild cost has not been profiled separately.

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
