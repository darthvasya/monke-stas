// ConceptProjection, pass 2: renders the mesh in UV space and adds one view's image, projected along the view, into
// an accumulation target: rgb += colour * weight, a += weight. Weight = visible from the view * facing it^sharpness
// * image alpha * boost; _HeadOnly = 1 (side and back views) drops it below the head. _MaskMode = 1 writes the fur
// mask instead (front view only): 0 on the face, 1 elsewhere.
Shader "Hidden/Stasik/ProjBake"
{
    Properties
    {
        _DepthTex ("Depth", 2D) = "white" {}
        _ViewTex ("View image", 2D) = "white" {}
    }

    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _DepthTex;
            sampler2D _ViewTex;
            sampler2D _OrigTex;     // the model's own texture (finds the shirt)
            sampler2D _ModelRows;   // per model row (this view): (left, right) of the silhouette, 0..1; -1 = empty
            sampler2D _ImageRows;   // per image row: (left, right) in image uv; -1 = empty
            float4x4 _ModelMatrix;
            float3 _Right;
            float3 _Forward;
            float4 _ViewRect;
            float _MinForward;
            float4 _ImgRect;        // character rect in the image uv: xy = min, zw = size
            float _DepthEps;
            float _HeadFrom;
            float _Sharpness;
            float _BodySharpness;
            float _HeadOnly;
            float _Boost;
            float _FaceBoost;       // extra weight on the face (front view)
            float4 _FaceEllipse;    // front image uv: xy = centre, zw = radii
            float _MaskMode;
            float _ShirtWeight;     // front view: weight of the shirt texels (stripes all the way round)
            sampler2D _StripeTex;   // front view: shirt colour per image row (1 x image height); alpha 0 = unknown

            float Chroma(float3 c)
            {
                return max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
            }

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float3 normal : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = float4(v.uv * 2 - 1, 0.5, 1);
                #if UNITY_UV_STARTS_AT_TOP
                o.pos.y = -o.pos.y;
                #endif
                o.uv = v.uv;
                o.world = mul(_ModelMatrix, v.vertex).xyz;
                o.normal = mul((float3x3)_ModelMatrix, v.normal);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 p = (float2(dot(i.world, _Right), i.world.y) - _ViewRect.xy) / _ViewRect.zw;
                float depth = tex2D(_DepthTex, p).r;
                float visible = step(dot(i.world, _Forward) - _MinForward, depth + _DepthEps);

                // Image coordinates: vertical by the silhouette rects; horizontal row by row for the head
                // (model silhouette edges -> image silhouette edges), by the rects for the body.
                float iv = _ImgRect.y + p.y * _ImgRect.w;
                float iu = _ImgRect.x + p.x * _ImgRect.z;
                float2 m = tex2D(_ModelRows, float2(p.y, 0.5)).rg;
                float2 c = tex2D(_ImageRows, float2(iv, 0.5)).rg;
                float head = smoothstep(_HeadFrom - 0.03, _HeadFrom + 0.03, p.y);
                if (m.x >= 0 && c.x >= 0 && m.y > m.x)
                    iu = lerp(iu, lerp(c.x, c.y, (p.x - m.x) / (m.y - m.x)), head);
                float4 img = tex2D(_ViewTex, float2(iu, iv));

                float faceDist = length((float2(iu, iv) - _FaceEllipse.xy) / _FaceEllipse.zw);
                float face = (1.0 - smoothstep(0.9, 1.25, faceDist)) * step(0.5, _FaceBoost);
                float facing = saturate(dot(normalize(i.normal), -_Forward));
                // Body (shirt, legs): the views' stripes and fur don't line up between photos, so the front photo only
                // covers what really faces it and fades into the generated texture, whose stripes are continuous.
                float sharpness = lerp(_BodySharpness, _Sharpness, head);
                // On the face the front photo wins where the surface really looks forward; turned-away parts (cheeks,
                // sides of the nose) blend into the side views instead of stretching the front photo.
                float facingTerm = lerp(pow(facing, sharpness), smoothstep(0.1, 0.4, facing), face);
                // Side and back views are used on the head only (ear, fur on the nape).
                facingTerm *= lerp(1.0, smoothstep(_HeadFrom - 0.04, _HeadFrom + 0.02, p.y), _HeadOnly);

                if (_MaskMode > 0.5)
                {
                    float onFace = visible * step(0.05, facing) * (1.0 - smoothstep(0.75, 1.3, faceDist));
                    return float4(onFace, 0, 0, 0); // accumulated with blending; 1 - this = fur allowed
                }

                // Shirt = texels the model's own texture shows without colour, up to the collar; for the front view
                // also only where the photo at the texel's position shows the shirt (not the hands or legs). The
                // photo's stripes are horizontal, so every shirt texel - also on the sides and the back, which the
                // front camera doesn't see - takes the stripes at its own height: from the photo where it faces the
                // front camera, elsewhere from the photo's stripe profile (_StripeTex: one colour per image row,
                // continued behind the chin). Other views stay off the shirt: their stripes don't line up with these.
                // _ShirtWeight = the composite's coverage, so a texel that is half shirt gets half the stripes.
                float shirt = (1.0 - smoothstep(0.06, 0.12, Chroma(sqrt(tex2D(_OrigTex, i.uv).rgb))))
                    * (1.0 - smoothstep(_HeadFrom + 0.01, _HeadFrom + 0.04, p.y));
                float wShirt = 0;
                float3 shirtColor = 0;
                if (_ShirtWeight > 0)
                {
                    shirt *= step(0.5, img.a) * (1.0 - smoothstep(0.06, 0.12, Chroma(sqrt(img.rgb))));
                    float4 stripe = tex2D(_StripeTex, float2(0.5, iv));
                    float photo = visible * smoothstep(0.35, 0.6, facing);
                    shirtColor = lerp(stripe.rgb, img.rgb, photo);
                    wShirt = shirt * _ShirtWeight * max(stripe.a, photo);
                }

                float w = visible * facingTerm * img.a * _Boost * (1.0 + _FaceBoost * face) * (1.0 - shirt);
                return float4(img.rgb * w + shirtColor * wShirt, w + wShirt);
            }
            ENDCG
        }
    }
}
