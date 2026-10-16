# Architecture and v0.1 scope

## Responsibilities

- `SceneLayerVisual` owns serialized settings, source collection, capture textures, image processing and the composite quad. Public settings and their serialized names remain stable.
- `SceneLayerVisualRendering` coordinates camera callbacks, registered groups, capture order and deferred resource release. Registration is rebuilt across Play Mode transitions, including when domain/scene reload is disabled.
- `SceneLayerVisualCaptureState` owns temporary visibility changes for one synchronous capture batch: Renderers, Canvas UI, Light2D volumes and Scene View gizmos. Its idempotent restoration runs from a `finally` block and from shutdown cleanup.

A nearer SceneLayerVisual defines a source-collection boundary. SpriteMask is a clipping dependency, not artwork. Use sibling effect groups; nested composition is outside v0.1 support.

## Capture lifecycle

1. Find visible groups with non-neutral effects before allocating helpers or scanning the rest of the scene.
2. Hide eligible outside Renderers once per camera batch. Keep SpriteMasks available and leave pre-hidden, disabled and camera-excluded inputs untouched.
3. Temporarily suppress Canvas and standalone Light2D volumes, which do not use ordinary Renderers. Keep lights illuminating the source sprites. Disable capture gizmos while preserving the viewer's toggle.
4. Capture each group's artwork with a hidden orthographic Game camera. Match the viewer's renderer and shadows; disable helper post-processing, volume mask, antialiasing and display-only HDR/XR output. Game camera semantics also apply to Scene View previews.
5. Grade and blur the captured HDR image, then restore outside scenery, UI, light volumes and gizmos before drawing the viewer. Show each processed quad at its group's sorting position; restore original group visibility after the viewer finishes.

Capture requests and blits restore the active RenderTexture even on failure. Failed batches restore original rendering and report the exception once; later cameras can retry. Changes to scene visibility do not write source materials, textures, Unity Layers or Renderer enabled flags.

A render callback may disable, re-enable or destroy a component. The coordinator keeps the current capture snapshot stable and defers resource destruction until its synchronous request has finished. Disabled or pending-release groups do not show a composite. Helpers use HideAndDontSave; component destruction, assembly reload, editor shutdown and Play Mode transitions release them.

## Processing and scaling

Processing ping-pongs two owned textures; pure blur skips color grading. Gaussian weights are normalized on the CPU only when the sample count changes. Capture requests and state lists are reused. Hierarchy membership is recollected per camera to support additions, removals and reparenting.

The coordinator still scans scene Renderers, Canvas and Light2D components once per active camera batch and renders one additional camera per visible effect group. Canvas enable changes may trigger UI rebuilds. These costs remain relevant in large scenes; eliminating them requires a rendering architecture change and profiling against the target scene.

New independent render inputs belong in the capture-state owner with paired suppression/restoration. New image effects belong in processing. Custom renderer features need separate camera filtering or validation; arbitrary features are not disabled globally.

## Deliberately outside v0.1

- Nested effect groups, perspective, stereo/XR and camera stacking.
- Sprite children interleaving with external artwork after compositing.
- General post-processing for arbitrary 3D geometry, additive materials or world-space UI.
- Gamma color space, mobile/WebGL compatibility and a universal performance budget.
- Presets, animation helpers and additional effects.

Neutral settings bypass processing. Disabling the component restores source artwork and releases its resources. Do not install duplicate script copies alongside the package.
