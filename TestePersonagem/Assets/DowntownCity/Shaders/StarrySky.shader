Shader "City/StarryNightSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (.006, .014, .038, 1)
        _HorizonColor ("Horizon", Color) = (.055, .08, .14, 1)
        _StarIntensity ("Star brightness", Range(0, 5)) = 2.4
        _MoonDirection ("Moon direction", Vector) = (.77, .53, -.36, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            float4 _ZenithColor, _HorizonColor, _MoonDirection;
            float _StarIntensity;
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float stars(float2 uv, float2 density, float threshold)
            {
                float2 grid = uv * density;
                float2 cell = floor(grid);
                float seed = hash(cell);
                float2 offset = .15 + .7 * float2(hash(cell + 17.3), hash(cell + 39.7));
                float2 delta = frac(grid) - offset;
                float radius = lerp(.045, .15, hash(cell + 8.5));
                float distanceToStar = length(delta);
                float aa = max(fwidth(distanceToStar), .015);
                return (1 - smoothstep(radius - aa, radius + aa, distanceToStar))
                    * step(threshold, seed) * lerp(.4, 1, hash(cell + 5));
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.direction);
                float elevation = saturate(d.y);
                float3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(elevation, .45));
                float2 uv = float2(atan2(d.z, d.x) * .159154943 + .5, asin(clamp(d.y, -1, 1)) * .318309886 + .5);
                float field = stars(uv, float2(580, 290), .986)
                    + .45 * stars(uv + .31, float2(930, 465), .993);
                color += field * _StarIntensity * float3(.8, .87, 1) * smoothstep(.015, .2, d.y);
                float moonAngle = acos(clamp(dot(d, normalize(_MoonDirection.xyz)), -1, 1));
                float moonDisc = 1 - smoothstep(.013, .016, moonAngle);
                float halo = exp(-moonAngle * 17) * .09;
                float craters = .9 + .1 * sin(d.x * 1700) * sin(d.z * 1400);
                color += float3(.64, .73, 1) * halo + moonDisc * float3(.89, .92, 1) * craters;
                color = lerp(float3(.012, .018, .03), color, smoothstep(-.12, .02, d.y));
                return float4(color, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
