# Architecture and v0.1 scope

`SceneLayerVisual` collects eligible child Renderers; a nearer SceneLayerVisual defines a separate collection boundary. SpriteMask is a clipping dependency and is not treated as artwork.

The rendering coordinator subscribes to URP camera rendering. For each visible active effect group it captures that group's renderers with a hidden orthographic camera, leaving SpriteMasks available. It applies exposure/color grading and optional separable Gaussian blur to HDR render textures. A hidden screen-aligned quad displays the processed premultiplied-alpha image at the group's sorting position.

Original renderers are temporarily hidden through `forceRenderingOff`, with their prior values restored after rendering or on failure. Materials, textures, Unity Layers and enabled flags are not rewritten.

Helpers use HideAndDontSave. Component disable/destruction, editor assembly reload, editor shutdown and play-mode transitions release resources. Runtime initialization rebuilds registration to support repeated Play Mode entry with scene/domain reload disabled.

The component checkbox controls processing. Neutral settings bypass processing without changing source artwork. Color, blur and quality fields retain their serialized names. Do not install duplicate script copies alongside the package.

## Deliberately outside v0.1

- Nested effect groups, perspective, stereo/XR, camera stacking.
- Sprite children interleaving with external artwork after compositing.
- General post-processing for arbitrary 3D geometry, additive materials or world-space UI.
- Gamma color space, mobile/WebGL compatibility and a universal performance budget.
- Presets, animation helpers and additional effects.

The coordinator collects visible effect groups before allocating capture helpers or scanning the scene. One active-renderer scan hides eligible artwork while keeping SpriteMask and pre-hidden/disabled/camera-excluded objects untouched. Captures then toggle only group members. Outside artwork is restored once before rendering the viewing camera; processed members are restored after it.

Processing ping-pongs the capture and result textures. Pure blur skips color grading. Gaussian weights are calculated and normalized on the CPU only when the sample count changes; the shader reuses symmetric weights. The capture request object is reused. Hierarchy membership is deliberately recollected per camera so runtime additions, removals and reparenting remain visible.

A global scan and one extra camera render per visible effect group remain. Removing them entirely requires a different rendering architecture and is outside this optimization.
