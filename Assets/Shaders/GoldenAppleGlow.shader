Shader "SnackRun/GoldenAppleGlow"
{
    // Hammy's golden carrot (originally written for the golden apple).
    Properties
    {
        [Header(Gold Body)]
        [HDR] _BaseColor ("Gold Colour", Color) = (1, 0.72, 0.16, 1)
        [HDR] _DeepColor ("Deep Gold (shadowed side)", Color) = (0.62, 0.34, 0.05, 1)
        [HDR] _HighlightColor ("Bright Gold Highlight", Color) = (1, 0.93, 0.62, 1)
        _Brightness ("Overall Brightness", Range(0, 5)) = 1.2
        _PulseSpeed ("Pulse Speed", Range(0, 10)) = 2.2
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.12
        _RimPower ("Fresnel Power", Range(0.5, 8)) = 2.5
        _RimIntensity ("Glossy Rim Intensity", Range(0, 3)) = 0.75
        _SpecPower ("Specular Sharpness", Range(4, 128)) = 40
        _SpecIntensity ("Specular Intensity", Range(0, 3)) = 0.8

        [Header(Enchantment Glint)]
        [HDR] _GlintColor ("Glint Colour", Color) = (1, 0.85, 0.38, 1)
        _GlintStrength ("Glint Strength", Range(0, 4)) = 1.1
        _GlintScale ("Glint Band Scale", Range(0.5, 20)) = 2.6
        _GlintSpeed ("Glint Scroll Speed", Range(0, 5)) = 0.9

        [Header(Enchant Purple Tint)]
        [HDR] _PurpleColor ("Enchant Purple", Color) = (0.62, 0.28, 1, 1)
        _PurpleGlint ("Purple in Glint Bands", Range(0, 1)) = 0.35
        _PurpleSheen ("Purple Sheen on Body", Range(0, 1)) = 0.12
        _PurpleAura ("Purple at Aura Edge", Range(0, 1)) = 0.4

        [Header(Sparkles)]
        _SparkleDensity ("Sparkle Density", Range(1, 30)) = 5
        _SparkleIntensity ("Sparkle Intensity", Range(0, 6)) = 2.0

        [Header(Glow Aura)]
        [HDR] _AuraColor ("Aura Colour", Color) = (1, 0.72, 0.18, 1)
        _AuraWidth ("Aura Width (object space)", Range(0, 2)) = 0.28
        _AuraPower ("Aura Softness", Range(0.5, 8)) = 2.2
        _AuraIntensity ("Aura Intensity", Range(0, 4)) = 0.65
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        CGINCLUDE
        #include "UnityCG.cginc"

        float4 _BaseColor;
        float4 _DeepColor;
        float4 _HighlightColor;
        float _Brightness;
        float _PulseSpeed;
        float _PulseAmount;
        float _RimPower;
        float _RimIntensity;
        float _SpecPower;
        float _SpecIntensity;

        float4 _GlintColor;
        float _GlintStrength;
        float _GlintScale;
        float _GlintSpeed;

        float4 _PurpleColor;
        float _PurpleGlint;
        float _PurpleSheen;
        float _PurpleAura;

        float _SparkleDensity;
        float _SparkleIntensity;

        float4 _AuraColor;
        float _AuraWidth;
        float _AuraPower;
        float _AuraIntensity;

        // Two overlapping sine waves - same breathing pulse as the garden lights.
        float EnchantPulse()
        {
            float wave1 = sin(_Time.y * _PulseSpeed);
            float wave2 = sin(_Time.y * _PulseSpeed * 1.9 + 2.1);
            return 1.0 + (wave1 * 0.6 + wave2 * 0.4) * _PulseAmount;
        }

        float Hash31(float3 p)
        {
            return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }
        ENDCG

        // ---------------------------------------------------------------
        // PASS 1: solid polished gold body
        // ---------------------------------------------------------------
        Pass
        {
            Name "GoldenBody"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
                float3 objPos : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);
                float ndotv = saturate(dot(n, v));
                float pulse = EnchantPulse();

                // ---- METALLIC GOLD: fake environment reflection. Surfaces reflecting "up" catch bright gold, surfaces reflecting "down" sink to a deep amber.
                float3 r = reflect(-v, n);
                float env = r.y * 0.5 + 0.5;
                float3 gold = lerp(_DeepColor.rgb, _BaseColor.rgb, smoothstep(0.05, 0.75, env));
                gold = lerp(gold, _HighlightColor.rgb, smoothstep(0.8, 1.0, env) * 0.55);

                // ---- SPECULAR from a fixed overhead "moonlight" direction ----
                float3 keyDir = normalize(float3(0.3, 1.0, 0.2));
                float3 h = normalize(v + keyDir);
                float spec = pow(saturate(dot(n, h)), _SpecPower);

                // ---- GLOSSY FRESNEL RIM ----
                float fresnel = pow(1.0 - ndotv, _RimPower);

                // ---- ENCHANTMENT GLINT: Minecraft-style diagonal shimmer bands sweeping across the item in two directions at different speeds, broken up slightly so.
                float3 p = i.objPos;
                float d1 = dot(p, float3(0.70, 0.70, 0.20)) * _GlintScale - _Time.y * _GlintSpeed * 2.0;
                float d2 = dot(p, float3(-0.60, 0.50, 0.62)) * _GlintScale * 1.35 + _Time.y * _GlintSpeed * 1.3;
                float band1 = pow(saturate(sin(d1) * 0.5 + 0.5), 6.0);
                float band2 = pow(saturate(sin(d2) * 0.5 + 0.5), 9.0);
                float streak = 0.65 + 0.35 * sin(dot(p, float3(7.1, 3.3, 5.7)) + _Time.y * 0.7);
                float glint = saturate(band1 + band2 * 0.75) * streak;

                // ---- TWINKLING SPARKLES: random cells flash on and off ----
                float3 sp = p * _SparkleDensity;
                float3 cell = floor(sp);
                float rnd = Hash31(cell);
                float3 local = frac(sp) - 0.5;
                float dotShape = saturate(1.0 - length(local) * 2.6);
                float twinkle = pow(saturate(sin(_Time.y * (3.0 + rnd * 5.0) + rnd * 6.2831)), 10.0);
                float sparkle = step(0.8, rnd) * dotShape * dotShape * twinkle;

                float3 col = gold * _Brightness * pulse;
                col += spec * _SpecIntensity * _HighlightColor.rgb;
                col += fresnel * _RimIntensity * _HighlightColor.rgb * pulse;
                // Glint bands shift slightly toward enchantment purple, and a
                // faint violet sheen creeps in at grazing angles - just a
                // hint, so the carrot still reads as gold first.
                float3 glintCol = lerp(_GlintColor.rgb, _PurpleColor.rgb, _PurpleGlint);
                col += glint * _GlintStrength * glintCol;
                col += fresnel * _PurpleSheen * _PurpleColor.rgb;
                col += sparkle * _SparkleIntensity * _HighlightColor.rgb;

                return fixed4(col, 1.0);
            }
            ENDCG
        }

        // --------------------------------------------------------------- PASS 2: soft golden aura (additive inflated shell) Drawing the shell's BACK faces.
        Pass
        {
            Name "EnchantAura"
            Cull Front
            ZWrite Off
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 inflated = v.vertex.xyz + normalize(v.normal) * _AuraWidth;
                o.vertex = UnityObjectToClipPos(float4(inflated, 1.0));
                float3 worldPos = mul(unity_ObjectToWorld, float4(inflated, 1.0)).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);
                float facing = saturate(-dot(n, v)); // back faces: 1 behind the centre, 0 at the outer edge
                float glow = pow(facing, _AuraPower) * _AuraIntensity * EnchantPulse();
                // Gold right against the carrot, drifting toward purple at
                // the halo's faint outer edge.
                float3 auraCol = lerp(_PurpleColor.rgb, _AuraColor.rgb, lerp(1.0 - _PurpleAura, 1.0, facing));
                return fixed4(auraCol * glow, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
