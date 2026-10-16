Shader "Scene Layer Visual"
{
    Properties { _MainTex ("Layer", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _Tint;
        float _Exposure, _TintStrength, _Saturation, _Contrast;
        float2 _BlurStep;
        float _BlurTapCount;
        float _BlurWeights[33];
        struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
        Varyings Vert(Attributes v)
        {
            Varyings o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o;
        }
        float4 Composite(Varyings i) : SV_Target { return tex2D(_MainTex, i.uv); }
        float4 Grade(Varyings i) : SV_Target
        {
            float4 c = tex2D(_MainTex, i.uv);
            // The transparent camera render is already premultiplied. Grade straight RGB,
            // then premultiply again so additive lightening cannot color transparent pixels.
            float3 rgb = c.a > 0.00001 ? c.rgb / c.a : 0;
            rgb *= _Exposure;
            rgb = lerp(dot(rgb, float3(0.2126, 0.7152, 0.0722)).xxx, rgb, _Saturation);
            rgb = max(0, (rgb - 0.18) * _Contrast + 0.18);
            rgb = lerp(rgb, _Tint.rgb, _TintStrength);
            return float4(rgb * c.a, c.a);
        }
        float4 SampleLayer(float2 uv)
        {
            if (any(uv < 0) || any(uv > 1)) return 0;
            return tex2D(_MainTex, uv);
        }
        float4 Blur(Varyings i) : SV_Target
        {
            float4 c = SampleLayer(i.uv) * _BlurWeights[0];
            int taps = clamp((int)_BlurTapCount, 6, 32);
            [loop] for (int tap = 1; tap <= taps; tap++)
            {
                float offset = (float)tap / taps;
                c += (SampleLayer(i.uv + _BlurStep * offset) + SampleLayer(i.uv - _BlurStep * offset)) * _BlurWeights[tap];
            }
            return c;
        }
        ENDHLSL
        Pass
        {
            Name "Composite"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
        Pass
        {
            Name "Grade"
            Tags { "LightMode"="SceneLayerProcessing" }
            Blend Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Grade
            ENDHLSL
        }
        Pass
        {
            Name "Blur"
            Tags { "LightMode"="SceneLayerProcessing" }
            Blend Off ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Blur
            ENDHLSL
        }
    }
}
