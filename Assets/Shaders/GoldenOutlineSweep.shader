Shader "SnackRun/GoldenOutlineSweep"
{
    // The golden "power-up" glow Bandit gets right after eating Hammy's golden carrot.
    Properties
    {
        [HDR] _Color ("Gold Colour", Color) = (1, 0.74, 0.2, 1)
        [HDR] _PurpleColor ("Enchant Purple (outer edge)", Color) = (0.62, 0.28, 1, 1)
        _PurpleAmount ("Purple at Outline Edge", Range(0, 1)) = 0.3
        _Intensity ("Intensity (driven by script)", Range(0, 4)) = 1
        _OutlineWidth ("Outline Width (world units, set by script)", Float) = 0.03
        _SweepY ("Sweep Line Height (world Y, set by script)", Float) = 0
        _SweepSoftness ("Sweep Edge Softness (world units, set by script)", Float) = 0.05
        _BandBoost ("Sweep Band Brightness", Range(0, 4)) = 1.6
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _BodyWash ("Gold Wash Over Body", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }

        CGINCLUDE
        #include "UnityCG.cginc"

        float4 _Color;
        float4 _PurpleColor;
        float _PurpleAmount;
        float _Intensity;
        float _OutlineWidth;
        float _SweepY;
        float _SweepSoftness;
        float _BandBoost;
        float _RimPower;
        float _BodyWash;

        // 1 for anything ABOVE the sweep line (already covered), 0 below it,
        // plus a bright band right at the line itself.
        float SweepMask(float worldY)
        {
            float soft = max(_SweepSoftness, 0.0001);
            float covered = smoothstep(_SweepY - soft, _SweepY + soft, worldY);
            float band = exp(-abs(worldY - _SweepY) / soft) * _BandBoost;
            return covered + band;
        }

        struct appdata
        {
            float4 vertex : POSITION;
            float3 normal : NORMAL;
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float3 worldNormal : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
        };
        ENDCG

        Pass
        {
            Name "OutlineHalo"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                // Inflate in WORLD space so the outline is the same thickness
                // regardless of the rig's own (unusual) transform scales.
                worldPos += normalize(worldNormal) * _OutlineWidth;
                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.worldNormal = worldNormal;
                o.worldPos = worldPos;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float facing = saturate(-dot(n, v)); // 1 right against his body, 0 at the outline's outer edge
                float glow = pow(facing, 1.4);
                float3 col = lerp(_PurpleColor.rgb, _Color.rgb, lerp(1.0 - _PurpleAmount, 1.0, facing));
                return fixed4(col * glow * SweepMask(i.worldPos.y) * _Intensity, 1.0);
            }
            ENDCG
        }

        Pass
        {
            Name "RimSheen"
            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float amount = (rim * 0.9 + _BodyWash) * SweepMask(i.worldPos.y) * _Intensity;
                return fixed4(_Color.rgb * amount, 1.0);
            }
            ENDCG
        }
    }
}
