Shader "SnackRun/TVScreen"
{
    Properties
    {
        [HDR] _ScreenColor ("Screen Colour", Color) = (1,1,1,1)
        _Brightness ("Brightness", Range(0,5)) = 1.5
        [Toggle] _EnableFlicker ("Enable Flicker", Float) = 0
        _FlickerSpeed ("Flicker Speed", Range(0,20)) = 8
        _FlickerStrength ("Flicker Strength", Range(0,1)) = 0.2
        _ScanlineSpeed ("Scanline Speed", Range(0,20)) = 10
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "TVScreenPass"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _ScreenColor;
            float _Brightness;
            float _EnableFlicker;
            float _FlickerSpeed;
            float _FlickerStrength;
            float _ScanlineSpeed;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ---- IDLE STATE: gentle glow/flicker on the blue screen ----
                // Always active, independent of the broken-TV effect below.
                float idleFlicker = 1.0 + sin(_Time.y * 3.0) * 0.08; // slow, subtle wobble
                float3 baseColour = _ScreenColor.rgb * _Brightness * idleFlicker;

                // ---- BROKEN STATE: black/white static with rolling bars ----
                float2 noiseUV = i.uv * float2(1.0, 50.0) + _Time.y * _FlickerSpeed;
                float noise = frac(sin(dot(noiseUV, float2(12.9898, 78.233))) * 43758.5453);

                float barPattern = frac(i.uv.y * 20.0 + _Time.y * _ScanlineSpeed);
                float bars = step(0.5, barPattern);

                float staticValue = saturate(noise * 0.7 + bars * 0.3);
                float3 staticColour = float3(staticValue, staticValue, staticValue);

                float flicker = 1.0 + sin(_Time.y * _FlickerSpeed * 3.0) * _FlickerStrength;
                staticColour *= flicker;

                // Blend: 0 = idle glowing blue, 1 = broken black/white static
                float3 finalColour = lerp(baseColour, staticColour, _EnableFlicker);

                return fixed4(finalColour, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Unlit/Color"
}