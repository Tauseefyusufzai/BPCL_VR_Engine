Shader "Custom/OilFill"
{
    Properties
    {
        _OilColor       ("Oil Color",        Color)       = (0.784, 0.659, 0.294, 0.85)
        _FillLevel      ("Fill Level",       Range(0,1))  = 0
        _BottomY        ("Cavity Bottom Y",  Float)       = -0.05
        _TopY           ("Cavity Top Y",     Float)       =  0.05
        _SurfaceShimmer ("Surface Shimmer",  Range(0,1))  = 0.3
        _Smoothness     ("Smoothness",       Range(0,1))  = 0.75
        _Metallic       ("Metallic",         Range(0,1))  = 0.05
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "OilFillPass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OilColor;
                float  _FillLevel;
                float  _BottomY;
                float  _TopY;
                float  _SurfaceShimmer;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // World-space fill threshold
                float fillHeight = lerp(_BottomY, _TopY, _FillLevel);

                // Discard all fragments above the fill line — this IS the fill effect
                clip(fillHeight - IN.positionWS.y);

                // Surface shimmer: bright highlight at the oil surface line
                float distToSurface = fillHeight - IN.positionWS.y;
                float surfaceFactor = 1.0 - saturate(distToSurface * 40.0);
                float shimmer       = surfaceFactor * _SurfaceShimmer;

                // Basic URP diffuse lighting
                float3 normal    = normalize(IN.normalWS);
                Light  mainLight = GetMainLight();
                float  NdotL     = saturate(dot(normal, mainLight.direction));
                float3 diffuse   = mainLight.color * NdotL;

                float3 col = (_OilColor.rgb + shimmer) * (diffuse + 0.35);

                // Smooth alpha at the surface edge
                float edgeAlpha = saturate(distToSurface * 20.0);
                float alpha     = _OilColor.a * lerp(0.6, 1.0, edgeAlpha);

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}