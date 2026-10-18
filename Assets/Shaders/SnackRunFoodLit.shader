// Used for small food-prop materials (cookie, muffin, etc.) that need to read as their real colour even in the House scene's dim, blue-tinted nighttime ambient light.
Shader "SnackRun/FoodLit"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _EmissionStrength ("Emission Floor (0-1)", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert

        struct Input
        {
            float2 uv_MainTex;
        };

        fixed4 _Color;
        half _EmissionStrength;

        void surf(Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _Color.rgb;
            o.Emission = _Color.rgb * _EmissionStrength;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
