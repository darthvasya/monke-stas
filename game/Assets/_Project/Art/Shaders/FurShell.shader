// Shell fur for URP: one material, drawn once per shell by FurShells (each shell gets its own _ShellHeight).
// Fur grows only where the base texture looks like fur (hue/saturation/value mask), outside the face sphere and
// not under a worn cap (Stasik.Characters.HeadCap sets the cap globals while the cap is on).
Shader "Stasik/FurShell"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _FurLength ("Fur Length (m)", Float) = 0.02
        _Density ("Strand Density (per UV unit)", Float) = 600
        _Thickness ("Strand Thickness", Range(0.05, 1)) = 0.7
        _Gravity ("Gravity", Range(0, 2)) = 0.4
        _RootDarkening ("Root Darkening", Range(0, 1)) = 0.45
        _LightInfluence ("Scene Light Influence (match Stasik/CharacterPhoto)", Range(0, 1)) = 0.35
        _ReferenceLight ("Reference Light", Float) = 2.0
        [Header(Fur Mask)]
        _HueMax ("Max Hue (0..1, orange ~0.1)", Range(0, 0.3)) = 0.12
        _MinSaturation ("Min Saturation", Range(0, 1)) = 0.35
        _MinValue ("Min Value", Range(0, 1)) = 0.12
        _MaxValue ("Max Value", Range(0, 1)) = 0.92
        [Header(Face Exclusion (object space))]
        _FaceCenter ("Face Center", Vector) = (0, 0, 0, 0)
        _FaceRadius ("Face Radius", Float) = 0
        [HideInInspector] _ShellHeight ("Shell Height", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "FurShell"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _FurLength;
                float _Density;
                float _Thickness;
                float _Gravity;
                float _RootDarkening;
                float _LightInfluence;
                float _ReferenceLight;
                float _HueMax;
                float _MinSaturation;
                float _MinValue;
                float _MaxValue;
                float4 _FaceCenter;
                float _FaceRadius;
                float _ShellHeight;
            CBUFFER_END

            // Globals (HeadCap): world -> cap space (origin on the band, y up) and crown radii; w = 1 while worn.
            float4x4 _CapWorldToLocal;
            float4 _CapRadii;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 rootWS : TEXCOORD4;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float3 RgbToHsvFast(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + 1e-5)), d / (q.x + 1e-5), q.x);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float h = _ShellHeight;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.rootWS = positionWS;
                positionWS += normalWS * (_FurLength * h);
                positionWS.y -= _Gravity * _FurLength * h * h;

                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.normalWS = normalWS;
                o.positionWS = positionWS;
                o.positionOS = input.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float h = _ShellHeight;
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // Where is fur: orange/brown, saturated, not too dark (hands) or bright (skin, shirt).
                float3 hsv = RgbToHsvFast(baseColor.rgb);
                float isHue = step(hsv.x, _HueMax) + step(0.97, hsv.x);
                float mask = saturate(isHue) * step(_MinSaturation, hsv.y) * step(_MinValue, hsv.z) * step(hsv.z, _MaxValue);
                mask *= step(_FaceRadius, distance(input.positionOS, _FaceCenter.xyz));
                // Under the cap: roots above the band plane and near the crown (strands would poke through it).
                float3 capPos = mul(_CapWorldToLocal, float4(input.rootWS, 1.0)).xyz;
                float underCap = _CapRadii.w * step(0.0, capPos.y) * step(length(capPos / max(_CapRadii.xyz, 1e-4)), 1.5);
                mask *= 1.0 - underCap;
                float furAmount = baseColor.a; // baked fur mask: 0 = face (no fur), fades in around it
                mask *= step(0.05, furAmount);

                // One strand per UV cell, random length, tapering towards the tip.
                float2 cell = input.uv * _Density;
                float2 id = floor(cell);
                float2 f = frac(cell) - 0.5;
                float rnd = Hash21(id);
                float strandLength = lerp(0.55, 1.0, rnd) * furAmount; // shorter strands towards the face
                float radius = 0.5 * _Thickness * saturate(1.0 - h / strandLength);
                clip(mask - 0.5);
                clip(strandLength - h);
                clip(radius - length(f));

                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float wrap = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                float3 lighting = mainLight.color * wrap * mainLight.shadowAttenuation + SampleSH(normalWS);
                float3 factor = lerp(1.0, lighting / _ReferenceLight, _LightInfluence);
                float occlusion = lerp(1.0 - _RootDarkening, 1.0, h);

                return half4(baseColor.rgb * factor * occlusion, 1.0);
            }
            ENDHLSL
        }
    }
}
