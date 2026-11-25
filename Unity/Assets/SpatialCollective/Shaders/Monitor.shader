Shader "SpatialCollective/Monitor"
{
    Properties { _MainTex("Live view",2D)="black" {} _Opacity("Viewport opacity",Range(0,1))=1 }
    SubShader
    {
        Tags { "Queue"="Transparent+501" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed _Opacity;
            struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;
            }
            fixed4 frag(v2f i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return fixed4(tex2D(_MainTex,i.uv).rgb,_Opacity);}
            ENDCG
        }
    }
}
