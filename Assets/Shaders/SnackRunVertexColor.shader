// Minimal shader for low-poly models whose colour comes from per-vertex paint rather than a textured/multi-material setup.
Shader "SnackRun/VertexColorLit"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert

        struct Input
        {
            float4 vertColor;
        };

        fixed4 _Color;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertColor = v.color;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = (IN.vertColor.rgb * _Color.rgb);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
