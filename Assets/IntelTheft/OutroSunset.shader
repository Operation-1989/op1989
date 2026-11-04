Shader "Operation1989/Departure Sunset"
{
    Properties
    {
        _Blend ("Dusk transition", Range(0,1)) = 0
        _SunDirection ("Sun direction", Vector) = (0.9,0.05,0.35,0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Blend;
            float4 _SunDirection;
            struct Varyings { float4 vertex : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings vert(float4 vertex : POSITION)
            {
                Varyings o;
                o.vertex = UnityObjectToClipPos(vertex);
                o.direction = mul((float3x3)unity_ObjectToWorld, vertex.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 direction = normalize(i.direction);
                float elevation = saturate(direction.y);
                float3 zenith = lerp(float3(.24,.31,.39), float3(.035,.027,.075), _Blend);
                float3 horizon = lerp(float3(.48,.48,.34), float3(.53,.16,.045), _Blend);
                float3 sky = lerp(horizon, zenith, pow(elevation, .42));
                float glow = pow(saturate(dot(direction, normalize(_SunDirection.xyz))), 32);
                float disc = smoothstep(.9995, .99985, dot(direction, normalize(_SunDirection.xyz)));
                sky += float3(.5,.18,.045) * glow * lerp(.22,.55,_Blend);
                sky += float3(1,.45,.13) * disc * .8;
                sky = lerp(sky, float3(.035,.025,.028), smoothstep(0, .12, -direction.y));
                return half4(sky,1);
            }
            ENDHLSL
        }
    }
}
