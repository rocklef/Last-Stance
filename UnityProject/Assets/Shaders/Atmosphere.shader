// Atmosphere: a full-screen pass (URP Full Screen Pass renderer feature, before post-processing)
// that gives the temple its air.
//   HEIGHT FOG   exponential fog that pools in the streets and thins with height, integrated along
//                every view ray, tinted from a cool haze to warm sunlight toward the low sun
//   GOD RAYS     the bright sunset sky is smeared radially toward the sun, so light streams around
//                the roofs, the trees and Moros's swarm
// Parameters are set every frame by Atmosphere.cs.
Shader "Hidden/LastStance/Atmosphere"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Atmosphere"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _AtmoSunDir;     // xyz: direction toward the sun (world), w: god ray strength
            float4 _AtmoSunColor;   // rgb: sunlight colour (HDR)
            float4 _AtmoFogColor;   // rgb: haze colour away from the sun, a: density
            float4 _AtmoFogHeight;  // x: base height, y: height falloff, z: max opacity, w: distance where fog starts
            float4 _AtmoRays;       // x: sky brightness threshold, y: decay, z: ray length, w: enabled

            half3 SampleColor(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb; }

            float DeviceDepth(float2 uv)
            {
                #if UNITY_REVERSED_Z
                    return SampleSceneDepth(uv);
                #else
                    return lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif
            }

            bool IsSky(float2 uv)
            {
                return Linear01Depth(SampleSceneDepth(uv), _ZBufferParams) > 0.9995;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = SampleColor(uv);
                float3 toSun = normalize(_AtmoSunDir.xyz);

                // ---------------------------------------------------------------- height fog
                if (!IsSky(uv))
                {
                    float3 wp = ComputeWorldSpacePosition(uv, DeviceDepth(uv), UNITY_MATRIX_I_VP);
                    float3 v = wp - _WorldSpaceCameraPos;
                    float dist = length(v);
                    float3 vd = v / max(dist, 1e-4);
                    float d = max(0, dist - _AtmoFogHeight.w);
                    float k = _AtmoFogHeight.y;
                    float h0 = (_WorldSpaceCameraPos.y + vd.y * _AtmoFogHeight.w) - _AtmoFogHeight.x;
                    float dy = vd.y * d;
                    // density exp(-k*h) integrated along the ray
                    float lineInt = abs(dy) > 1e-3 ? (exp(-k * h0) - exp(-k * (h0 + dy))) / (k * vd.y) : exp(-k * h0) * d;
                    float fog = 1 - exp(-_AtmoFogColor.a * max(0, lineInt));
                    fog = min(fog, _AtmoFogHeight.z);
                    float sunAmt = pow(saturate(dot(vd, toSun)), 6);
                    half3 fogCol = lerp(_AtmoFogColor.rgb, _AtmoSunColor.rgb * 0.55, sunAmt);
                    col = lerp(col, fogCol, fog);
                }

                // ---------------------------------------------------------------- god rays
                if (_AtmoRays.w > 0.5)
                {
                    float4 sc = mul(UNITY_MATRIX_VP, float4(_WorldSpaceCameraPos + toSun * 1000, 1));
                    if (sc.w > 0)
                    {
                        float2 sunUV = sc.xy / sc.w * 0.5 + 0.5;
                        #if UNITY_UV_STARTS_AT_TOP
                            sunUV.y = 1 - sunUV.y;
                        #endif
                        // fade as the sun leaves the screen
                        float2 off = abs(sunUV - 0.5);
                        float vis = saturate(1.5 - max(off.x, off.y) * 1.6);
                        if (vis > 0.001)
                        {
                            const int N = 40;
                            float2 delta = (uv - sunUV) * (_AtmoRays.z / N);
                            float2 p = uv;
                            float decay = 1, illum = 0;
                            [loop] for (int i = 0; i < N; i++)
                            {
                                p -= delta;
                                float s = IsSky(p) ? 1 : 0;
                                float b = s * saturate(Luminance(SampleColor(p)) - _AtmoRays.x);
                                illum += b * decay;
                                decay *= _AtmoRays.y;
                            }
                            illum /= N;
                            col += _AtmoSunColor.rgb * illum * vis * _AtmoSunDir.w;
                        }
                    }
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
