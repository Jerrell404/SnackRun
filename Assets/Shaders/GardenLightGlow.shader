Shader "SnackRun/GardenLightGlow"
{
    Properties
    {
        [HDR] _CoreColor ("Core Glow Colour", Color) = (1, 0.95, 0.75, 1)
        [HDR] _RimColor ("Outer Glow Colour", Color) = (1, 0.55, 0.15, 1)
        _GlowRadius ("Core Radius", Range(0.05, 1)) = 0.35
        _Brightness ("Overall Brightness", Range(0, 5)) = 2.0
        _PulseSpeed ("Pulse Speed", Range(0, 10)) = 1.5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.15
        _RimPower ("Fresnel Power", Range(0.5, 8)) = 4.0
        _RimIntensity ("Fresnel Intensity", Range(0, 3)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "GardenLightPass"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _CoreColor;
            float4 _RimColor;
            float _GlowRadius;
            float _Brightness;
            float _PulseSpeed;
            float _PulseAmount;
            float _RimPower;
            float _RimIntensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ---- RADIAL GRADIENT: bright core fading to warm rim colour ----
                float2 centered = i.uv - 0.5;
                float dist = length(centered) * 2.0;
                float radialT = saturate(dist / _GlowRadius);
                float3 gradientColour = lerp(_CoreColor.rgb, _RimColor.rgb, radialT);

                // ---- FRESNEL: glass-edge highlight, view-angle dependent ----
                float fresnel = pow(1.0 - saturate(dot(normalize(i.worldNormal), normalize(i.viewDir))), _RimPower);

                // ---- ORGANIC FLICKER: two overlapping sine waves, not a single clean pulse ----
                float wave1 = sin(_Time.y * _PulseSpeed);
                float wave2 = sin(_Time.y * _PulseSpeed * 2.3 + 1.7);
                float flicker = 1.0 + (wave1 * 0.65 + wave2 * 0.35) * _PulseAmount;

                float3 finalColour = gradientColour * _Brightness * flicker;
                finalColour += fresnel * _RimIntensity * _RimColor.rgb;

                return fixed4(finalColour, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}