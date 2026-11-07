// 루미나 크로니클 — 명중 이펙트 전용 가산 셰이더 (베기 명중 v3 플립북).
// 출력 = 텍스처 × 색(tint) × 정점색, 블렌드 SrcAlpha One → 화면 += rgb × alpha (알파는 블렌드에서 한 번만 곱함).
// 텍스처는 흰색 RGB + straight alpha. 밝기 배율(_Intensity)은 RGB 에만 곱한다.
// LightMode 태그·키워드가 없는 단순 unlit 셰이더라 URP(SRPDefaultUnlit)와 Built-in 양쪽에서 그려지고, 빌드 변형 제거 위험이 없다.
// UV 칸(4×4 프레임)은 렌더러별 MaterialPropertyBlock 의 _MainTex_ST 로 지정한다.
Shader "Lumina/FxAdditive"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _Intensity;

            struct a2v
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert (a2v v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.rgb *= _Intensity;
                return c;
            }
            ENDCG
        }
    }
}
