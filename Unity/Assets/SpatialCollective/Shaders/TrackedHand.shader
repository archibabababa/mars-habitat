Shader "SpatialCollective/TrackedHand"
{
    Properties { _Color("Hand colour",Color)=(.82,.72,.62,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+600" "RenderType"="Opaque" }
        Cull Back ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata {float4 vertex:POSITION;float3 normal:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION;float3 normal:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
            fixed4 _Color;
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float light=.60+.40*saturate(dot(normalize(i.normal),normalize(float3(-.4,.8,-.5))));
                return fixed4(_Color.rgb*light,1);
            }
            ENDCG
        }
    }
}
