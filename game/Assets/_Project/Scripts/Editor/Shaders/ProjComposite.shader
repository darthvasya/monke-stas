// ConceptProjection, pass 3: weighted average of all views over the original texture; alpha = fur mask.
Shader "Hidden/Stasik/ProjComposite"
{
    Properties
    {
        _MainTex ("Original", 2D) = "white" {}
        _AccumTex ("Accumulated views", 2D) = "black" {}
        _FaceMaskTex ("Face mask", 2D) = "black" {}
    }

    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _AccumTex;
            sampler2D _FaceMaskTex;
            float _Coverage; // accumulated weight at which the views fully replace the original texture

            float4 frag(v2f_img i) : SV_Target
            {
                float4 orig = tex2D(_MainTex, i.uv);
                float4 acc = tex2D(_AccumTex, i.uv);
                float3 views = acc.a > 1e-4 ? acc.rgb / acc.a : orig.rgb;
                float k = saturate(acc.a / _Coverage);
                float fur = 1.0 - saturate(tex2D(_FaceMaskTex, i.uv).r);
                return float4(lerp(orig.rgb, views, k), fur);
            }
            ENDCG
        }
    }
}
