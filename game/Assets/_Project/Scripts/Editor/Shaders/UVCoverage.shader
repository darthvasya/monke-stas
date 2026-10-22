// FrontProjectionBaker: marks the texels the mesh covers in UV space (for edge dilation).
Shader "Hidden/Stasik/UVCoverage"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert(float2 uv : TEXCOORD0) : SV_POSITION
            {
                float4 pos = float4(uv * 2 - 1, 0.5, 1);
                #if UNITY_UV_STARTS_AT_TOP
                pos.y = -pos.y;
                #endif
                return pos;
            }

            float4 frag() : SV_Target
            {
                return 1;
            }
            ENDCG
        }
    }
}
