Shader "SpatialCollective/SpatialTint"
{
    Properties
    {
        _Color("Tint",Color)=(0.2,0.8,1,0.2)
        _SCGeometry("Use model node visibility",Float)=0
        _SCInteriorOpacity("Inhabited view opacity",Range(0,1))=0
        _SCCurrentOpacity("Current cabin white opacity (zero preserves glass)",Range(0,1))=0
        _SCWhiteShading("White surface relief",Range(0,1))=0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source",Float)=5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination",Float)=10
        [Toggle] _ZWrite("Depth write",Float)=0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth test",Float)=4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite [_ZWrite] ZTest [_ZTest] Blend [_SrcBlend] [_DstBlend]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            struct appdata {float4 vertex:POSITION; float2 node:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct v2f {float4 pos:SV_POSITION; float visibility:TEXCOORD0; float current:TEXCOORD1; float3 world:TEXCOORD2; float floor:TEXCOORD3; UNITY_VERTEX_OUTPUT_STEREO};
            fixed4 _Color;
            float _SCGeometry,_SCFilterEnabled,_SCInteriorView,_SCInteriorOpacity,_SCOverlayView;
            float _SCCurrentNode,_SCCurrentOpacity,_SCWhiteShading;
            float _SCNodeVisibility[32];
            v2f vert(appdata v)
            {
                v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=UnityObjectToClipPos(v.vertex);
                o.visibility=(_SCGeometry>.5&&_SCFilterEnabled>.5)?_SCNodeVisibility[clamp((int)(v.node.x+.5),0,31)]:1;
                o.current=(_SCGeometry>.5&&_SCFilterEnabled>.5&&abs(v.node.x-_SCCurrentNode)<.1)?1:0;
                o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.floor=v.node.y;
                return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);clip(i.visibility-.001);fixed4 c=_Color;
                // The panorama supplies the local floor; linked cameras keep the complete model.
                if(_SCGeometry>.5&&_SCFilterEnabled>.5&&_SCOverlayView>.5&&i.floor>.5)discard;
                if(_SCGeometry>.5 && _SCFilterEnabled>.5 && _SCInteriorView>.5)c.a=max(c.a,_SCInteriorOpacity);
                // Imported node IDs identify only the inhabited cabin, not its whole branch.
                // Glass has zero white opacity and remains transparent to the Mars panorama.
                if(i.current>.5 && _SCCurrentOpacity>0)
                {
                    float3 n=cross(ddx(i.world),ddy(i.world));n*=rsqrt(max(dot(n,n),.000001));
                    float relief=.72+.28*abs(dot(n,normalize(float3(.3,.8,.5))));
                    c.rgb=float3(.95,.96,.97)*lerp(1,relief,_SCWhiteShading);
                    if(_SCOverlayView<.5)c.a=max(c.a,_SCCurrentOpacity);
                }
                c.a*=i.visibility;return c;
            }
            ENDCG
        }
    }
}
