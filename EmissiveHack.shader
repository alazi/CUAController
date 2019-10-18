// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)

Shader "Custom/Alazi/ExtraEmissiveComputeBuff" {
	Properties{
		_Color("Color", Color) = (0.5,0.5,0.5,0.5)
		_MainTex("Texture", 2D) = "white" {}
		_AlphaAdjust("Alpha Adjust", Range(-1, 1)) = 0
	}

		Category{
			Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
			Blend SrcAlpha One
			ColorMask RGB
			Cull Off Lighting Off ZWrite Off

			SubShader {
				Pass {

					CGPROGRAM
					#pragma vertex vert
					#pragma fragment frag
					#pragma target 4.0
					#pragma multi_compile_particles
					#pragma multi_compile_fog

					#include "UnityCG.cginc"

					sampler2D _MainTex;
					fixed4 _Color;

					struct appdata_t {
						float4 vertex : POSITION;
						fixed4 color : COLOR;
						float2 texcoord : TEXCOORD0;
						uint id : SV_VertexID;
						UNITY_VERTEX_INPUT_INSTANCE_ID
					};

					struct v2f {
						float4 vertex : SV_POSITION;
						fixed4 color : COLOR;
						float2 texcoord : TEXCOORD0;
						UNITY_FOG_COORDS(1)
						UNITY_VERTEX_OUTPUT_STEREO
					};

					float _AlphaAdjust;

					float4 _MainTex_ST;
					StructuredBuffer<float3> verts;
					v2f vert(appdata_t v)
					{
						v2f o;
						UNITY_SETUP_INSTANCE_ID(v);
						UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
						//verts[v.id]
						o.vertex = UnityObjectToClipPos(verts[v.id]);
	
						o.color = v.color;
						o.color.a = saturate(o.color.a + _AlphaAdjust);
						o.texcoord = TRANSFORM_TEX(v.texcoord,_MainTex);
						UNITY_TRANSFER_FOG(o,o.vertex);
						return o;
					}

					UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);


					fixed4 frag(v2f i) : SV_Target
					{


						fixed4 col = i.color * _Color * tex2D(_MainTex, i.texcoord);

						UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0,0,0,0)); // fog towards black due to our blend mode
						return col;
					}
					ENDCG
				}
			}
		}
}
