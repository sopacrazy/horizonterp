Shader "Horizonte/Portal Glow"
{
    Properties
    {
        _Color ("Cor emissiva", Color) = (1, .75, .08, .5)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            Lighting Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
    Fallback Off
}
