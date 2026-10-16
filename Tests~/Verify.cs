// Execute with Unity Pipeline eval_file, in Edit Mode or Play Mode.
// Creates temporary objects only; does not save or replace the user's scene.
var root = new UnityEngine.GameObject("SceneLayerVisual verification");
root.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
var texture = new UnityEngine.Texture2D(32, 32, UnityEngine.TextureFormat.RGBA32, false);
var pixels = new UnityEngine.Color[1024];
for (int i = 0; i < pixels.Length; i++) pixels[i] = UnityEngine.Color.white;
texture.SetPixels(pixels); texture.Apply();
var sprite = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, 32, 32), new UnityEngine.Vector2(.5f, .5f), 32);
var material = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
var output = new UnityEngine.RenderTexture(512, 256, 24, UnityEngine.RenderTextureFormat.ARGBHalf);
output.Create();
var reader = new UnityEngine.Texture2D(512, 256, UnityEngine.TextureFormat.RGBAFloat, false, true);
var previousActive = UnityEngine.RenderTexture.active;
var cameraObject = new UnityEngine.GameObject("Verification camera");
cameraObject.transform.SetParent(root.transform);
var camera = cameraObject.AddComponent<UnityEngine.Camera>();
camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 2;
camera.transform.position = new UnityEngine.Vector3(0, 0, -10);
camera.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
camera.backgroundColor = UnityEngine.Color.black;
camera.cullingMask = 1 << 30;
camera.targetTexture = output;
var cameraData = cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
cameraData.renderPostProcessing = false;
var leftObject = new UnityEngine.GameObject("Left layer");
leftObject.transform.SetParent(root.transform);
leftObject.layer = 30; leftObject.transform.position = new UnityEngine.Vector3(-1.5f, 0, 0);
leftObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
var leftRenderer = leftObject.AddComponent<UnityEngine.SpriteRenderer>();
leftRenderer.sprite = sprite; leftRenderer.sharedMaterial = material;
var left = leftObject.AddComponent<SceneLayerVisuals.SceneLayerVisual>();
left.resolution = SceneLayerVisuals.SceneLayerVisual.Resolution.Full;
var rightObject = new UnityEngine.GameObject("Right layer");
rightObject.transform.SetParent(root.transform);
rightObject.layer = 30; rightObject.transform.position = new UnityEngine.Vector3(1.5f, 0, 0);
rightObject.AddComponent<UnityEngine.Rendering.SortingGroup>();
var rightRenderer = rightObject.AddComponent<UnityEngine.SpriteRenderer>();
rightRenderer.sprite = sprite; rightRenderer.sharedMaterial = material;
var right = rightObject.AddComponent<SceneLayerVisuals.SceneLayerVisual>();
var results = new System.Collections.Generic.List<string>();
try
{
    for (int renderer = 0; renderer < 2; renderer++)
    {
        cameraData.SetRenderer(renderer);
        left.enabled = false; right.enabled = false;
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
        UnityEngine.RenderTexture.active = output;
        reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
        float baseline = reader.GetPixel(160, 128).r;
        if (baseline < .9f) throw new System.Exception("Baseline sprite missing: " + baseline);

        left.enabled = true; left.blurRadius = 16;
        right.enabled = true; right.tint = UnityEngine.Color.red; right.tintStrength = 1;
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
        UnityEngine.RenderTexture.active = output;
        reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
        float outside = reader.GetPixel(124, 128).r;
        var rightColor = reader.GetPixel(352, 128);
        if (outside < .01f || outside > .8f) throw new System.Exception("Blur outside edge invalid: " + outside + "; center=" + reader.GetPixel(160, 128) + "; right=" + rightColor);
        if (rightColor.r < .9f || rightColor.g > .05f) throw new System.Exception("Independent tint invalid: " + rightColor);
        if (leftRenderer.forceRenderingOff || rightRenderer.forceRenderingOff)
            throw new System.Exception("Source renderer visibility leaked after render.");
        var helperBefore = UnityEngine.Object.FindObjectsByType<UnityEngine.MeshRenderer>(UnityEngine.FindObjectsSortMode.None);
        foreach (var helper in helperBefore)
            if (helper.name.StartsWith("SceneLayerVisual:") && helper.enabled)
                throw new System.Exception("Composite renderer leaked after render.");
        var bytes = reader.EncodeToPNG();
        System.IO.Directory.CreateDirectory("Temp/SceneLayerVisual");
        System.IO.File.WriteAllBytes("Temp/SceneLayerVisual/effects-renderer-" + renderer + ".png", bytes);

        left.tint = UnityEngine.Color.red; left.tintStrength = 1;
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
        UnityEngine.RenderTexture.active = output;
        reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
        var gradedBlurCenter = reader.GetPixel(160, 128);
        var gradedBlurEdge = reader.GetPixel(124, 128);
        if (gradedBlurCenter.r < .9f || gradedBlurCenter.g > .02f || gradedBlurEdge.r < .01f || gradedBlurEdge.g > .02f)
            throw new System.Exception("Combined color grading and blur failed: " + gradedBlurCenter + "; edge=" + gradedBlurEdge);

        foreach (var quality in new[] {
            SceneLayerVisuals.SceneLayerVisual.Resolution.Half,
            SceneLayerVisuals.SceneLayerVisual.Resolution.Quarter })
        {
            left.resolution = quality;
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
            UnityEngine.RenderTexture.active = output;
            reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
            if (reader.GetPixel(124, 128).r < .01f || reader.GetPixel(352, 128).g > .05f)
                throw new System.Exception("Downsampled independent blur failed: " + quality);
        }
        left.resolution = SceneLayerVisuals.SceneLayerVisual.Resolution.Full;

        foreach (float radius in new[] { 48f, 96f, 256f })
        {
            left.blurRadius = radius;
            // Exercise the same validation used when changing the Inspector field.
            left.SendMessage("OnValidate");
            if (left.blurRadius != radius) throw new System.Exception("Extended blur radius was clamped: " + radius);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
            UnityEngine.RenderTexture.active = output;
            reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
            if (reader.GetPixel(100, 128).r < .005f)
                throw new System.Exception("Extended blur did not reach beyond the old radius: " + radius);
            if (reader.GetPixel(352, 128).g > .05f)
                throw new System.Exception("Extended blur changed the other layer.");
            System.IO.File.WriteAllBytes("Temp/SceneLayerVisual/blur-" + radius + "-renderer-" + renderer + ".png", reader.EncodeToPNG());
        }

        left.blurRadius = 0; leftRenderer.color = UnityEngine.Color.black;
        left.tint = UnityEngine.Color.white; left.tintStrength = .5f;
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
        UnityEngine.RenderTexture.active = output;
        reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
        float lightened = reader.GetPixel(160, 128).r;
        if (lightened < .4f || lightened > .8f) throw new System.Exception("Black lightening invalid: " + lightened);

        left.tintStrength = 0; left.exposure = 0; leftRenderer.color = new UnityEngine.Color(.25f,.25f,.25f,1);
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
        UnityEngine.RenderTexture.active = output;
        reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
        float unexposed = reader.GetPixel(160, 128).r;
        left.exposure = 1;
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
        UnityEngine.RenderTexture.active = output;
        reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
        float exposed = reader.GetPixel(160, 128).r;
        if (unexposed < .001f || exposed < unexposed * 1.8f || exposed > unexposed * 2.2f)
            throw new System.Exception("Exposure should double linear brightness: " + unexposed + " -> " + exposed);
        left.exposure = 0; leftRenderer.color = UnityEngine.Color.black;

        left.enabled = false; right.enabled = false;
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
            new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = output });
        UnityEngine.RenderTexture.active = output;
        reader.ReadPixels(new UnityEngine.Rect(0, 0, 512, 256), 0, 0); reader.Apply();
        if (reader.GetPixel(160, 128).r > .01f || reader.GetPixel(352, 128).g < .9f)
            throw new System.Exception("Disabling component did not restore original colors.");
        results.Add("Renderer " + renderer + ": independent and combined blur/tint, all resolutions, radii 48/96/256, exposure, black lightening, component bypass and visibility restoration passed.");
        leftRenderer.color = UnityEngine.Color.white;
        left.tintStrength = 0;
    }
    return new { playMode = UnityEditor.EditorApplication.isPlaying, checks = results };
}
finally
{
    UnityEngine.RenderTexture.active = previousActive;
    UnityEngine.Object.DestroyImmediate(root);
    output.Release();
    UnityEngine.Object.DestroyImmediate(output);
    UnityEngine.Object.DestroyImmediate(reader);
    UnityEngine.Object.DestroyImmediate(material);
    UnityEngine.Object.DestroyImmediate(sprite);
    UnityEngine.Object.DestroyImmediate(texture);
}
