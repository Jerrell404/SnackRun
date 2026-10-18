Shader "SnackRun/LaserBeam"
{
    // laser shader for the backyard lasers
    // glows, pulses and has a wave moving along the beam

    Properties
    {
        [HDR] _LaserColor ("Laser Colour", Color) = (1, 0, 0, 1) // HDR so it can go brighter than white for bloom
        _VertexPulse ("Vertex Pulse Amount", Range(0, 0.1)) = 0.02 // makes the beam slightly grow/shrink

        _Brightness ("Brightness", Range(0, 10)) = 2
        _PulseSpeed ("Pulse Speed", Range(0, 20)) = 4
        _PulseStrength ("Pulse Strength", Range(0, 1)) = 0.25 // 0 = no pulse, 1 = strong pulse
        _Opacity ("Opacity", Range(0, 1)) = 0.8

        // the wave that moves along the beam
        _MovementSpeed ("Movement Speed", Range(0, 10)) = 2
        _MovementStrength ("Movement Strength", Range(0, 1)) = 0.15

        _Softness ("Edge Softness", Range(0, 1)) = 0.5 // how much the edges fade out
    }

    SubShader
    {
        // transparent so it blends with the scene
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off // dont write depth since its see-through
        Cull Off   // show both sides, beams are thin

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            fixed4 _LaserColor;
            float _Brightness;
            float _PulseSpeed;
            float _PulseStrength;
            float _Opacity;
            float _MovementSpeed;
            float _MovementStrength;
            float _Softness;
            float _VertexPulse;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                // push verts out along the normal a bit so the beam "breathes"
                float pulse = sin(_Time.y * _PulseSpeed) * _VertexPulse;
                float3 displaced = v.vertex.xyz + v.normal * pulse;
                o.vertex = UnityObjectToClipPos(float4(displaced, 1.0));
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 laserColour = _LaserColor.rgb;

                // pulse brightness over time, sin is -1 to 1 so map it to 0 to 1
                float pulse = sin(_Time.y * _PulseSpeed);
                float pulseAmount = (pulse + 1.0) * 0.5;
                float pulseMultiplier = lerp(1.0 - _PulseStrength, 1.0, pulseAmount);

                // wave travelling along the beam (uv.x is along the length)
                float movingWave = sin((i.uv.x * 20.0) + (_Time.y * _MovementSpeed));
                float movementAmount = (movingWave + 1.0) * 0.5;
                float movementMultiplier = lerp(1.0 - _MovementStrength, 1.0, movementAmount);

                float finalBrightness = _Brightness * pulseMultiplier * movementMultiplier;
                laserColour *= finalBrightness;

                // fade the edges (uv.y goes across the width)
                float edgeFade = smoothstep(0.0, _Softness, i.uv.y) *
                                 smoothstep(1.0, 1.0 - _Softness, i.uv.y);

                float finalAlpha = _Opacity * edgeFade;

                return fixed4(laserColour, finalAlpha);
            }
            ENDCG
        }
    }

    FallBack "Transparent/VertexLit"
}
