Shader "Horizonte/Recovered City Surface"
{
    Properties
    {
        _MainTex ("Wall / side albedo", 2D) = "white" {}
        _TopTex ("Roof / ground albedo", 2D) = "white" {}
        _Color ("Wall tint", Color) = (1,1,1,1)
        _TopColor ("Roof / ground tint", Color) = (1,1,1,1)
        _MetersPerTile ("Wall metres per repeat", Range(0.25,24)) = 4
        _TopMetersPerTile ("Roof metres per repeat", Range(0.25,24)) = 4
        _Smoothness ("Smoothness", Range(0,1)) = 0.15
        _Metallic ("Metallic", Range(0,1)) = 0
        [Toggle] _Facade ("Procedural facade windows", Float) = 0
        _BuildingBaseY ("Building base world Y", Float) = 0
        _Seed ("Facade variation", Float) = 0
        _GroundBlend ("Ground colour variation", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 250
        Cull Back

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        sampler2D _TopTex;
        fixed4 _Color;
        fixed4 _TopColor;
        float _MetersPerTile;
        float _TopMetersPerTile;
        half _Smoothness;
        half _Metallic;
        half _Facade;
        float _BuildingBaseY;
        float _Seed;
        half _GroundBlend;
        half _RecoveredCityNight;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        // Signed distance to a rectangle in metres; derivative smoothing avoids
        // hard subpixel window edges when viewing the city from a distance.
        half RectangleMask(float2 position, float2 centre, float2 halfSize)
        {
            float2 distance = abs(position - centre) - halfSize;
            float edge = max(distance.x, distance.y);
            float aa = max(fwidth(edge), 0.015);
            return 1.0 - smoothstep(-aa, aa, edge);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 normal = normalize(IN.worldNormal);
            float3 weights = pow(abs(normal), 5.0);
            weights /= max(weights.x + weights.y + weights.z, 0.0001);
            float wallScale = rcp(max(_MetersPerTile, 0.25));
            float topScale = rcp(max(_TopMetersPerTile, 0.25));

            // World-space projections keep the texture density stable even on
            // recovered meshes whose original atlas assignments are unavailable.
            fixed3 wallX = tex2D(_MainTex, IN.worldPos.zy * wallScale).rgb;
            fixed3 wallZ = tex2D(_MainTex, IN.worldPos.xy * wallScale).rgb;
            fixed3 top = tex2D(_TopTex, IN.worldPos.xz * topScale).rgb;
            fixed3 albedo = (wallX * weights.x + wallZ * weights.z) * _Color.rgb
                          + top * weights.y * _TopColor.rgb;

            float groundVariation = sin(IN.worldPos.x * 0.071 + _Seed)
                                  * sin(IN.worldPos.z * 0.089 - _Seed);
            albedo *= 1.0 + groundVariation * _GroundBlend * 0.065;

            half smoothness = _Smoothness;
            half metallic = _Metallic;
            if (_Facade > 0.5)
            {
                // A metric facade grid is a replacement finish, not an attempt
                // to reproduce the missing original building texture atlas.
                float horizontal = abs(normal.x) > abs(normal.z) ? IN.worldPos.z : IN.worldPos.x;
                float height = IN.worldPos.y - _BuildingBaseY;
                float2 metric = float2(horizontal + _Seed * 0.731, height);
                float2 cellSize = float2(3.2, 3.1);
                float2 cell = floor(metric / cellSize);
                float2 local = frac(metric / cellSize) * cellSize;
                float2 centre = float2(1.6, 2.0);
                float2 paneSize = float2(0.63, 0.73);
                half pane = RectangleMask(local, centre, paneSize);
                half outer = RectangleMask(local, centre, paneSize + 0.07);
                half frame = saturate(outer - pane);

                half vertical = 1.0 - smoothstep(0.15, 0.45, abs(normal.y));
                half aboveBase = smoothstep(1.15, 1.30, height);
                float footprint = max(length(ddx(IN.worldPos)), length(ddy(IN.worldPos)));
                half detailFade = 1.0 - smoothstep(0.45, 1.1, footprint);
                half facadeMask = vertical * aboveBase * detailFade;
                pane *= facadeMask;
                frame *= facadeMask;

                float variation = frac(sin(dot(cell, float2(12.9898, 78.233)) + _Seed) * 43758.5453);
                fixed3 glass = lerp(fixed3(0.035,0.055,0.067), fixed3(0.11,0.15,0.17), variation);
                // Subtle upper reflection remains diffuse, avoiding glowing
                // rectangles and overbright glass on the daylight test map.
                glass += smoothstep(1.45, 2.7, local.y) * fixed3(0.018,0.026,0.035);
                albedo = lerp(albedo, fixed3(0.24,0.25,0.25), frame);
                albedo = lerp(albedo, glass, pane);
                smoothness = lerp(smoothness, 0.48, pane);
                metallic = lerp(metallic, 0.12, pane);
                o.Emission = fixed3(.75,.52,.28) * pane * step(variation, .38) * _RecoveredCityNight * .45;
            }

            o.Albedo = albedo;
            o.Smoothness = smoothness;
            o.Metallic = metallic;
            o.Occlusion = 1;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
