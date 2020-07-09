// Copyright (c) <2015> <Playdead>
// This file is subject to the MIT License as seen in the root of this folder structure (LICENSE.TXT)
// AUTHOR: Lasse Jon Fuglsang Pedersen <lasse@playdead.com>

Shader "custom_taa/motion_vector"
{
	CGINCLUDE
	//--- program begin

	#pragma only_renderers ps4 xboxone d3d11 d3d9 xbox360 opengl glcore gles3 metal vulkan
	#pragma target 3.0

	#pragma multi_compile CAMERA_PERSPECTIVE CAMERA_ORTHOGRAPHIC
	#pragma multi_compile __ TILESIZE_10 TILESIZE_20 TILESIZE_40
#pragma enable_d3d11_debug_symbols

	#include "UnityCG.cginc"
	#include "IncDepth.cginc"

#if UNITY_VERSION < 540
	uniform float4x4 _CameraToWorld;// UNITY_SHADER_NO_UPGRADE
#endif

#if UNITY_VERSION < 550
	#define STEREO_ARRAY
	#define STEREO_INDEX(x) x
#else
	#define STEREO_ARRAY [2]
	#define STEREO_INDEX(x) x[unity_StereoEyeIndex] 
#endif

	uniform sampler2D_half _VelocityTex;
	uniform float4 _VelocityTex_TexelSize;

	uniform float4 _ProjectionExtents STEREO_ARRAY;// xy = frustum extents at distance 1, zw = jitter at distance 1

	uniform float4x4 _CurrV STEREO_ARRAY;
	uniform float4x4 _CurrVP STEREO_ARRAY;
	uniform float4x4 _CurrM;

	uniform float4x4 _PrevVP STEREO_ARRAY;
	uniform float4x4 _PrevVP_NoFlip STEREO_ARRAY;
	uniform float4x4 _PrevM;

	struct v2f
	{
		float4 cs_pos : SV_POSITION;
		float4 ss_pos : TEXCOORD0;
		float3 cs_xy_curr : TEXCOORD1;
		float3 cs_xy_prev : TEXCOORD2;
	};

	v2f process_vertex(float4 ws_pos_curr, float4 ws_pos_prev)
	{
		v2f OUT;

		const float occlusion_bias = 0.03;

		OUT.cs_pos = mul(mul(STEREO_INDEX(_CurrVP), _CurrM), ws_pos_curr);
		OUT.ss_pos = ComputeScreenPos(OUT.cs_pos);
		OUT.ss_pos.z = -mul(mul(STEREO_INDEX(_CurrV), _CurrM), ws_pos_curr).z - occlusion_bias;// COMPUTE_EYEDEPTH
		OUT.cs_xy_curr = OUT.cs_pos.xyw;
		OUT.cs_xy_prev = mul(mul(STEREO_INDEX(_PrevVP), _PrevM), ws_pos_prev).xyw;

	#if UNITY_UV_STARTS_AT_TOP
		OUT.cs_xy_curr.y = -OUT.cs_xy_curr.y;
		OUT.cs_xy_prev.y = -OUT.cs_xy_prev.y;
	#endif

		return OUT;
	}

	v2f vert(appdata_base IN)
	{
		return process_vertex(IN.vertex, IN.vertex);
	}

	v2f vert_skinned(appdata_base IN)
	{
		return process_vertex(IN.vertex, float4(IN.normal, 1.0));// previous frame positions stored in normal data
	}

	float4 frag(v2f IN) : SV_Target
	{
		float2 ss_txc = IN.ss_pos.xy / IN.ss_pos.w;
		float scene_d = depth_sample_linear(ss_txc);

		// discard if occluded
		clip(scene_d - IN.ss_pos.z);

		// compute velocity in ndc
		float2 ndc_curr = IN.cs_xy_curr.xy / IN.cs_xy_curr.z;
		float2 ndc_prev = IN.cs_xy_prev.xy / IN.cs_xy_prev.z;

		// compute screen space velocity [0,1;0,1]
	#if UNITY_SINGLE_PASS_STEREO
		return float4(0.5 * (ndc_curr - ndc_prev) * unity_StereoScaleOffset[unity_StereoEyeIndex].xy, 0.0, 0.0);
	#else
		return float4(0.5 * (ndc_curr - ndc_prev), 0.0, 0.0);
	#endif
	}

	//--- program end
	ENDCG

	SubShader
	{
		// 0: vertices
		Pass
		{
			ZTest LEqual Cull Back ZWrite On
			Fog { Mode Off }

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag

			ENDCG
		}

		// 1: vertices skinned
		Pass
		{
			ZTest LEqual Cull Back ZWrite On
			Fog { Mode Off }

			CGPROGRAM

			#pragma vertex vert_skinned
			#pragma fragment frag

			ENDCG
		}
	}

	Fallback Off
}