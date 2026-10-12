Shader "Faceblur/Pixelate"
{
    Properties
    {
        _BlocksAcross ("Blocks across square", Float) = 6
        _Diameter ("World side length", Float) = 0.24
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }

        GrabPass { "_FaceblurGrab" }

        Pass
        {
            ZWrite Off
            Cull Off
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _FaceblurGrab;
            float _BlocksAcross;
            float _Diameter;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 grab : TEXCOORD0;      // this pixel
                float4 center : TEXCOORD1;    // square centre (same for all vertices)
                float depth : TEXCOORD2;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                // Camera-facing flat square: built in view space around the object's origin
                float4 cv = mul(UNITY_MATRIX_MV, float4(0, 0, 0, 1));
                float3 vp = cv.xyz;
                vp.xy += v.vertex.xy * _Diameter * 1.25;   // a bit larger, cells are trimmed in frag
                vp.z += _Diameter * 0.5;                    // pull toward camera so the head itself doesn't hide it

                o.pos = mul(UNITY_MATRIX_P, float4(vp, 1));
                o.grab = ComputeGrabScreenPos(o.pos);
                o.center = ComputeGrabScreenPos(mul(UNITY_MATRIX_P, cv));
                o.depth = -cv.z;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 res = _ScreenParams.xy;
                float2 uv = i.grab.xy / i.grab.w;
                float2 cuv = i.center.xy / i.center.w;

                // side of the square in pixels, whole-pixel block size
                float sidePx = _Diameter * unity_CameraProjection._m11 * res.y * 0.5 / max(i.depth, 0.05);
                float block = max(2.0, floor(sidePx / _BlocksAcross));

                float2 cell = floor(uv * res / block);
                float2 cellUV = (cell + 0.5) * block / res;

                // keep only whole cells that lie inside the square -> blocky square silhouette
                float2 d = abs(cellUV - cuv) * res;
                if (max(d.x, d.y) > sidePx * 0.5) discard;

                float2 step = block / res / 3.0;
                fixed4 sum = 0;
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        sum += tex2D(_FaceblurGrab, cellUV + float2(x, y) * step);

                fixed4 c = sum / 9.0;
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}
