Shader "SnackRun/FridgeLightGlow"
{
    Properties
    {
        [HDR] _BulbColor ("Bulb Core Colour", Color) = (1, 0.92, 0.72, 1)
        [HDR] _DiffuserColor ("Diffuser Glass Colour", Color) = (1, 0.83, 0.58, 1)
        _GlowRadius ("Core Radius", Range(0.05, 1)) = 0.45
        _Brightness ("Overall Brightness", Range(0, 5)) = 2.5
        _FlickerSpeed ("Flicker Speed", Range(0, 10)) = 6.0
        _FlickerAmount ("Flicker Amount", Range(0, 0.3)) = 0.04
        _RimPower ("Fresnel Power", Range(0.5, 8)) = 3.0
        _RimIntensity ("Fresnel Intensity", Range(0, 3)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "FridgeLightPass"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _BulbColor;
            float4 _DiffuserColor;
            float _GlowRadius;
            float _Brightness;
            float _FlickerSpeed;
            float _FlickerAmount;
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
                // ---- RADIAL GRADIENT: hot bulb core softening into the frosted diffuser colour ----
                // A real fridge bulb sits behind a small frosted/tinted plastic cover, so the
                // centre reads brighter and near-white while the edges pick up a warmer amber tint.
                float2 centered = i.uv - 0.5;
                float dist = length(centered) * 2.0;
                float radialT = saturate(dist / _GlowRadius);
                float3 gradientColour = lerp(_BulbColor.rgb, _DiffuserColor.rgb, radialT * radialT);

                // ---- FRESNEL: catches the curved edge of the plastic diffuser dome ----
                float fresnel = pow(1.0 - saturate(dot(normalize(i.worldNormal), normalize(i.viewDir))), _RimPower);

                // ---- SUBTLE FLICKER: a real incandescent bulb doesn't pulse, it just has a faint,
                // slightly irregular shimmer from the filament/old wiring - kept low-amplitude on purpose ----
                float wave1 = sin(_Time.y * _FlickerSpeed);
                float wave2 = sin(_Time.y * _FlickerSpeed * 4.1 + 0.6);
                float flicker = 1.0 + (wave1 * 0.7 + wave2 * 0.3) * _FlickerAmount;

                float3 finalColour = gradientColour * _Brightness * flicker;
                finalColour += fresnel * _RimIntensity * _DiffuserColor.rgb;

                return fixed4(finalColour, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
