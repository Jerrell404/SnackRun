Shader "SnackRun/AquariumWater"
{
    Properties
    {
        _WaterColor ("Water Tint", Color) = (0.2, 0.45, 0.5, 0.55)
        _DeepColor ("Edge Depth Tint", Color) = (0.05, 0.2, 0.25, 0.7)
        _RippleSpeed ("Ripple Speed", Range(0, 5)) = 0.5
        _RippleStrength ("Ripple Strength", Range(0, 0.02)) = 0.006
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.5
        _FresnelIntensity ("Fresnel Intensity", Range(0, 2)) = 0.6
        _Opacity ("Opacity", Range(0, 1)) = 0.65
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _WaterColor;
            float4 _DeepColor;
            float _RippleSpeed, _RippleStrength, _FresnelPower, _FresnelIntensity, _Opacity;

            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float3 worldNormal:TEXCOORD1; float3 viewDir:TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Subtle surface ripple - kept gentle so a large flat tank surface
                // doesn't look like it's boiling
                float2 ripple = i.uv + sin(i.uv.yx * 12.0 + _Time.y * _RippleSpeed) * _RippleStrength;
                float rippleShade = sin(ripple.x * 18.0) * 0.5 + 0.5;

                // Fresnel brightens the water near the glass edges, like real refraction
                float fresnel = pow(1.0 - saturate(dot(normalize(i.worldNormal), normalize(i.viewDir))), _FresnelPower);

                // Blend base tint toward a deeper colour at the edges
                float3 baseColour = lerp(_WaterColor.rgb, _DeepColor.rgb, fresnel * 0.5);
                float3 finalColour = baseColour + rippleShade * 0.02 + fresnel * _FresnelIntensity * 0.3;

                float alpha = saturate(_Opacity + fresnel * 0.2);
                return fixed4(finalColour, alpha);
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}