// Simple URP lit terrain: single grass albedo, Lambert + ambient + main light shadows.
Shader "Universal Render Pipeline/ProceduralTerrain"
{
    Properties
    {
        [NoScaleOffset] _GrassTex("Grass", 2D) = "white" {}
        [NoScaleOffset] _PathTex("Path", 2D) = "white" {}
        [NoScaleOffset] _RockTex("Rock", 2D) = "white" {}
        [NoScaleOffset] _SplatmapTex("Splat (R = path, G = rock, linear float)", 2D) = "black" {}
        _GrassTiling("Grass Tiling", Float) = 1
        _PathTiling("Path Tiling", Float) = 1
        _RockTiling("Rock Tiling", Float) = 1
        _HexSize("Hex Cell Size (UV)", Float) = 1
        _HexBlend("Hex Blend Sharpness", Float) = 10
        _EdgeNoiseScale("Path Edge Noise Scale (world)", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_GrassTex);
            SAMPLER(sampler_GrassTex);
            TEXTURE2D(_PathTex);
            SAMPLER(sampler_PathTex);
            TEXTURE2D(_RockTex);
            SAMPLER(sampler_RockTex);
            TEXTURE2D(_SplatmapTex);
            SAMPLER(sampler_SplatmapTex);

            CBUFFER_START(UnityPerMaterial)
                half _GrassTiling;
                half _PathTiling;
                half _RockTiling;
                half _HexSize;
                half _HexBlend;
                half _EdgeNoiseScale;
            CBUFFER_END

            // Stable hash for per-hex rotation / phase (no texture dependency).
            float2 HexHash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float HexHashAngle(float2 cell)
            {
                return HexHash22(cell).x * 6.28318530718;
            }

            float3 HexAxialToCube(float2 axial)
            {
                float q = axial.x;
                float r = axial.y;
                return float3(q, -q - r, r);
            }

            float2 HexCubeToAxial(float3 c)
            {
                return float2(c.x, c.z);
            }

            float3 HexCubeRound(float3 c)
            {
                float rx = round(c.x);
                float ry = round(c.y);
                float rz = round(c.z);

                float xDiff = abs(rx - c.x);
                float yDiff = abs(ry - c.y);
                float zDiff = abs(rz - c.z);

                if (xDiff > yDiff && xDiff > zDiff)
                    rx = -ry - rz;
                else if (yDiff > zDiff)
                    ry = -rx - rz;
                else
                    rz = -rx - ry;

                return float3(rx, ry, rz);
            }

            // Flat-top hex grid: inverse of axial_to_pixel (size = center-to-corner radius in UV space).
            float2 HexPixelToAxial(float2 p, float hexSize)
            {
                float inv = rcp(hexSize);
                float q = (2.0 / 3.0 * p.x) * inv;
                float r = (-1.0 / 3.0 * p.x + 0.5773502691896258 * p.y) * inv;
                return float2(q, r);
            }

            float2 HexAxialToPixel(float2 axial, float hexSize)
            {
                float x = hexSize * (1.5 * axial.x);
                float y = hexSize * (1.7320508075688772 * (axial.y + axial.x * 0.5));
                return float2(x, y);
            }

            // Soft Voronoi over 7 hex cells (center + 6 neighbors) with per-cell rotation to hide square tiling.
            half3 SampleTextureHex(float2 p, Texture2D tex, SamplerState samp)
            {
                float hexSize = max(0.0001, (float)_HexSize);
                float sharp = max(0.001, (float)_HexBlend);

                float2 a = HexPixelToAxial(p, hexSize);
                float3 cr = HexCubeRound(HexAxialToCube(a));
                float2 baseAxial = HexCubeToAxial(cr);

                half3 sum = half3(0, 0, 0);
                half wsum = 0;

                // Axial offsets: center + 6 neighbors on a flat-top hex grid.
                const int kCount = 7;
                const float2 kOffsets[7] =
                {
                    float2(0, 0),
                    float2(1, 0),
                    float2(1, -1),
                    float2(0, -1),
                    float2(-1, 0),
                    float2(-1, 1),
                    float2(0, 1)
                };

                UNITY_UNROLL
                for (int i = 0; i < kCount; i++)
                {
                    float2 cell = baseAxial + kOffsets[i];
                    float2 center = HexAxialToPixel(cell, hexSize);
                    float2 delta = p - center;
                    float dist2 = dot(delta, delta);

                    float ang = HexHashAngle(cell);
                    float c = cos(-ang);
                    float s = sin(-ang);
                    float2 rot = float2(c * delta.x - s * delta.y, s * delta.x + c * delta.y);

                    float2 phase = HexHash22(cell + float2(19.19, 47.7));
                    float2 texUV = frac(rot + phase);

                    half3 col = SAMPLE_TEXTURE2D(tex, samp, texUV).rgb;
                    half w = (half)exp(-sharp * dist2 / (hexSize * hexSize * 3.0));

                    sum += col * w;
                    wsum += w;
                }

                return sum / max(wsum, 1e-4);
            }

            // Value noise [0,1] for stochastic grass/path edges (patchy threshold vs splat).
            float TerrainHash01(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            float TerrainValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = TerrainHash01(i);
                float b = TerrainHash01(i + float2(1, 0));
                float c = TerrainHash01(i + float2(0, 1));
                float d = TerrainHash01(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Two octaves for smaller breakup without pure white noise.
            float TerrainEdgeNoise(float2 worldXZ, float scale)
            {
                float s = max(scale, 0.001);
                float2 uv = worldXZ * s;
                float n = TerrainValueNoise(uv) * 0.62 + TerrainValueNoise(uv * 2.18 + float2(13.7, 9.2)) * 0.38;
                return saturate(n);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 splatUV : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                const VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                const VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = half3(normalInput.normalWS);
                output.splatUV = input.uv0;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 grassUV = float2(input.positionWS.x, input.positionWS.z) * (float)_GrassTiling;
                float2 pathUV = float2(input.positionWS.x, input.positionWS.z) * (float)_PathTiling;
                float2 rockUV = float2(input.positionWS.x, input.positionWS.z) * (float)_RockTiling;
                // Splat RGBAFloat: R = path, G = rock (linear weights).
                float4 splat = SAMPLE_TEXTURE2D(_SplatmapTex, sampler_SplatmapTex, input.splatUV);
                float pathMask = saturate(splat.r);
                float rockMask = saturate(splat.g);
                const half3 grassCol = SampleTextureHex(grassUV, _GrassTex, sampler_GrassTex);
                const half3 pathCol = SAMPLE_TEXTURE2D(_PathTex, sampler_PathTex, pathUV).rgb;
                const half3 rockCol = SampleTextureHex(rockUV, _RockTex, sampler_RockTex);
                float2 wXZ = float2(input.positionWS.x, input.positionWS.z);
                float edgeN = TerrainEdgeNoise(wXZ, (float)_EdgeNoiseScale);
                float rockEdgeN = TerrainEdgeNoise(wXZ + float2(31.7, 12.4), (float)_EdgeNoiseScale);
                // Stochastic blend: P(layer) = mask; path wins over grass/rock.
                half rockPick = (half)step(rockEdgeN, rockMask);
                half pathPick = (half)step(edgeN, pathMask);
                const half3 baseCol = lerp(grassCol, rockCol, rockPick);
                const half3 albedo = lerp(baseCol, pathCol, pathPick);
                const half3 n = normalize(input.normalWS);

                const float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                const Light mainLight = GetMainLight(shadowCoord);
                const half3 radiance = mainLight.color * (half)mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                const half3 lit = albedo * LightingLambert(radiance, mainLight.direction, n);
                const half3 ambient = albedo * SampleSH(n);
                return half4(lit + ambient, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 GetShadowPositionHClip(ShadowAttributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
                float3 lightDirectionWS = _LightDirection;
#endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                positionCS = ApplyShadowClamping(positionCS);
                return positionCS;
            }

            ShadowVaryings ShadowPassVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = GetShadowPositionHClip(input);
                return output;
            }

            half4 ShadowPassFragment(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
