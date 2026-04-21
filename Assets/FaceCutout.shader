Shader "Custom/FaceCutout"
{
    Properties
    {
        _MainTex ("Webcam Texture", 2D) = "white" {}
        _MaskTex ("Mask Texture", 2D) = "white" {}
        _InvertMask ("Invert Mask", Float) = 0
        _EdgeSmoothing ("Edge Smoothing (blur)", Range(0, 1)) = 0.5
        _Threshold ("Threshold", Range(0, 1)) = 0.5
        _Dilation ("Dilation (expand)", Range(0, 0.1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        // הגדרות שקיפות
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _MaskTex;
            float4 _MainTex_ST;
            float4 _MaskTex_TexelSize;
            float _InvertMask;
            float _EdgeSmoothing;
            float _Threshold;
            float _Dilation;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // קריאת הצבע מהמצלמה
                fixed4 col = tex2D(_MainTex, i.uv);
                
                // Sample multiple points for edge smoothing (dilation effect)
                float2 offset = _MaskTex_TexelSize.xy * _Dilation;
                
                float maskAlpha = 0.0;
                
                // Sample center and neighbors to dilate/smooth edges
                float centerMask = max(max(tex2D(_MaskTex, i.uv).r, tex2D(_MaskTex, i.uv).g), tex2D(_MaskTex, i.uv).b);
                maskAlpha = centerMask;
                
                // Enhanced dilation - sample more points for better edge filling
                maskAlpha = max(maskAlpha, max(max(tex2D(_MaskTex, i.uv + float2(offset.x, 0)).r, tex2D(_MaskTex, i.uv + float2(-offset.x, 0)).r),
                                               max(tex2D(_MaskTex, i.uv + float2(0, offset.y)).r, tex2D(_MaskTex, i.uv + float2(0, -offset.y)).r)));
                
                // Additional diagonal sampling for better corner coverage
                maskAlpha = max(maskAlpha, max(max(tex2D(_MaskTex, i.uv + float2(offset.x, offset.y)).r, tex2D(_MaskTex, i.uv + float2(-offset.x, -offset.y)).r),
                                               max(tex2D(_MaskTex, i.uv + float2(offset.x, -offset.y)).r, tex2D(_MaskTex, i.uv + float2(-offset.x, offset.y)).r)));
                
                // Smooth edges with threshold and apply more aggressive smoothing
                maskAlpha = smoothstep(_Threshold - _EdgeSmoothing * 2.0, _Threshold + _EdgeSmoothing, maskAlpha);
                
                // Invert if needed
                if (_InvertMask > 0.5)
                    maskAlpha = 1.0 - maskAlpha;
                
                col.a = maskAlpha;

                return col;
            }
            ENDCG
        }
    }
}