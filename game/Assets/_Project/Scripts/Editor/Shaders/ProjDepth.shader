// ConceptProjection, pass 1: depth of the model seen from one view (orthographic along _Forward), min distance.
Shader "Hidden/Stasik/ProjDepth"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            BlendOp Min
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4x4 _ModelMatrix;
            float3 _Right;      // view right axis (image +x)
            float3 _Forward;    // view direction (camera looks along it)
            float4 _ViewRect;   // x = min along right, y = min height, z = size along right, w = size height
            float _MinForward;  // nearest distance along forward

            struct v2f
            {
                float4 pos : SV_POSITION;
                float d : TEXCOORD0;
            };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                float3 w = mul(_ModelMatrix, vertex).xyz;
                float2 p = (float2(dot(w, _Right), w.y) - _ViewRect.xy) / _ViewRect.zw;
                o.pos = float4(p * 2 - 1, 0.5, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.pos.y = -o.pos.y;
                #endif
                o.d = dot(w, _Forward) - _MinForward;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(i.d, 0, 0, 1);
            }
            ENDCG
        }
    }
}
