using System;
using System.Collections;
using System.IO;
using System.Linq;
using SceneLayerVisuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class SceneLayerVisualPlayerCheck : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (!Environment.GetCommandLineArgs().Contains("-slv-check")) return;
        var host = new GameObject("Scene Layer Visual player verification");
        host.AddComponent<SceneLayerVisualPlayerCheck>();
    }

    private IEnumerator Start()
    {
        // Let runtime initialization and the normal camera render execute first.
        for (int i = 0; i < 8; i++) yield return null;
        try
        {
            var camera = Camera.main;
            var effects = FindObjectsByType<SceneLayerVisual>(FindObjectsSortMode.None);
            if (effects.Length != 3) throw new InvalidOperationException("Demo component count: " + effects.Length);
            var shader = Resources.Load<Shader>("SceneLayerVisual");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Player shader missing/unsupported");
            var output = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGBHalf);
            output.Create();
            var pixels = new Texture2D(960, 540, TextureFormat.RGBA32, false, true);
            var previousActive = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = output;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = output });
                RenderTexture.active = output;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                pixels.Apply();
                var processed = pixels.GetPixels();
                string resultDirectory = Path.GetDirectoryName(Application.dataPath);
                File.WriteAllBytes(Path.Combine(resultDirectory, "demo-processed.png"), pixels.EncodeToPNG());
                foreach (var effect in effects) effect.enabled = false;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = output });
                RenderTexture.active = output;
                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                pixels.Apply();
                var original = pixels.GetPixels();
                float difference = 0;
                for (int i = 0; i < original.Length; i++)
                    difference += Mathf.Abs(processed[i].r - original[i].r) + Mathf.Abs(processed[i].g - original[i].g) + Mathf.Abs(processed[i].b - original[i].b);
                difference /= original.Length;
                File.WriteAllBytes(Path.Combine(resultDirectory, "demo-original.png"), pixels.EncodeToPNG());
                if (difference < .005f) throw new InvalidOperationException("Effects did not change player image: " + difference);
                foreach (var renderer in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                    if (renderer.forceRenderingOff) throw new InvalidOperationException("Player source visibility leaked");
                File.WriteAllText(Path.Combine(resultDirectory, "player-results.json"), "{\"passed\":true,\"meanRgbDifference\":" + difference.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
                var benchmarkGroups = new SceneLayerVisual[8];
                var backgroundObjects = new GameObject[2000];
                var prototype = effects.First(effect => effect.blurRadius > 0);
                try
                {
                    for (int i = 0; i < benchmarkGroups.Length; i++)
                    {
                        benchmarkGroups[i] = Instantiate(prototype.gameObject).GetComponent<SceneLayerVisual>();
                        benchmarkGroups[i].name = "Benchmark group " + i;
                        benchmarkGroups[i].enabled = false;
                    }
                    var timings = new System.Collections.Generic.List<string>();
                    var sourcePrototype = prototype.GetComponentInChildren<SpriteRenderer>();
                    foreach (int backgroundCount in new[] { 0, 2000 })
                    {
                        if (backgroundCount > 0)
                        {
                            for (int i = 0; i < backgroundObjects.Length; i++)
                            {
                                backgroundObjects[i] = new GameObject("Outside scenery " + i);
                                backgroundObjects[i].transform.position = new Vector3(1000 + i, 0, 0);
                                var renderer = backgroundObjects[i].AddComponent<SpriteRenderer>();
                                renderer.sprite = sourcePrototype.sprite;
                                renderer.sharedMaterial = sourcePrototype.sharedMaterial;
                            }
                        }
                        foreach (int groupCount in new[] { 0, 1, 4, 8 })
                        {
                            for (int i = 0; i < benchmarkGroups.Length; i++) benchmarkGroups[i].enabled = i < groupCount;
                            var samples = new double[30];
                            var requestSamples = new double[30];
                            var allocatedSamples = new long[30];
                            var timer = new System.Diagnostics.Stopwatch();
                            for (int sample = -5; sample < samples.Length; sample++)
                            {
                                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                                timer.Restart();
                                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = output });
                                double requestMs = timer.Elapsed.TotalMilliseconds;
                                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                                RenderTexture.active = output;
                                // Includes synchronized readback; request time is CPU-side submission, not isolated GPU time.
                                pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                                timer.Stop();
                                if (sample >= 0)
                                {
                                    samples[sample] = timer.Elapsed.TotalMilliseconds;
                                    requestSamples[sample] = requestMs;
                                    allocatedSamples[sample] = allocated;
                                }
                            }
                            Array.Sort(samples);
                            Array.Sort(requestSamples);
                            Array.Sort(allocatedSamples);
                            string Number(double number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            timings.Add("{\"backgroundRenderers\":" + backgroundCount + ",\"groups\":" + groupCount
                                + ",\"medianRenderReadbackMs\":" + Number((samples[14] + samples[15]) / 2)
                                + ",\"medianRequestMs\":" + Number((requestSamples[14] + requestSamples[15]) / 2)
                                + ",\"medianRequestAllocatedBytes\":" + ((allocatedSamples[14] + allocatedSamples[15]) / 2) + "}");
                        }
                    }
                    File.WriteAllText(Path.Combine(resultDirectory, "benchmark.json"), "[" + string.Join(",", timings) + "]");
                }
                finally
                {
                    foreach (var group in benchmarkGroups) if (group != null) { group.enabled = false; Destroy(group.gameObject); }
                    foreach (var background in backgroundObjects) if (background != null) Destroy(background);
                }
                Debug.Log("SLV_PLAYER_PASS: image difference=" + difference);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                output.Release();
                Destroy(output);
                Destroy(pixels);
            }
            Application.Quit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Application.Quit(1);
        }
    }
}
