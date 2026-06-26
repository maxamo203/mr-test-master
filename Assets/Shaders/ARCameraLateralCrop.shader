// Shader de fondo AR con recorte lateral para modo Cardboard estéreo.
// Cada ojo recibe la mitad del feed de cámara:
//   _CropOffsetX = 0.0  → ojo izquierdo (muestra la mitad izquierda del feed)
//   _CropOffsetX = 0.5  → ojo derecho   (muestra la mitad derecha del feed)
//
// Basado en los shaders de ARCore y ARKit de AR Foundation. Necesita estar en
// "Always Included Shaders" en Graphics Settings para no ser stripeado en IL2CPP.

Shader "Custom/ARCameraLateralCrop"
{
    Properties
    {
        _MainTex        ("Texture",      2D)    = "white" {}
        _EnvironmentDepth ("Depth",      2D)    = "black" {}
        _textureY       ("TextureY",     2D)    = "white" {}
        _textureCbCr    ("TextureCbCr",  2D)    = "black" {}
        _CropOffsetX    ("Crop X Offset", Float) = 0.0
        _CropScaleX     ("Crop X Scale",  Float) = 0.5
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SubShader 1: Android OpenGL ES 3 (OES external texture)
    // ─────────────────────────────────────────────────────────────────────────
    SubShader
    {
        Name "ARCamera LateralCrop GLES3"
        Tags { "Queue"="Background" "RenderType"="Background" "ForceNoShadowCasting"="True" }

        Pass
        {
            Cull Off ZTest Always ZWrite On Lighting Off
            Tags { "LightMode"="Always" }

            GLSLPROGRAM
            #pragma only_renderers gles3
            #include "UnityCG.glslinc"

#ifdef SHADER_API_GLES3
#extension GL_OES_EGL_image_external_essl3 : require
#endif

            uniform mat4  _UnityDisplayTransform;
            uniform float _CropOffsetX;
            uniform float _CropScaleX;

#ifdef VERTEX
            varying vec2 textureCoord;
            void main()
            {
#ifdef SHADER_API_GLES3
                gl_Position  = gl_ModelViewProjectionMatrix * gl_Vertex;
                textureCoord = (vec4(gl_MultiTexCoord0.x, gl_MultiTexCoord0.y, 1.0, 0.0) * _UnityDisplayTransform).xy;
#endif
            }
#endif

#ifdef FRAGMENT
            varying vec2 textureCoord;
            uniform samplerExternalOES _MainTex;
            uniform float _UnityCameraForwardScale;

#if defined(SHADER_API_GLES3) && !defined(UNITY_COLORSPACE_GAMMA)
            vec3 GammaToLinearSpace(vec3 sRGB)
            {
                return sRGB * (sRGB * (sRGB * 0.305306011 + 0.682171111) + 0.012522878);
            }
#endif

            float ConvertDistanceToDepth(float d)
            {
                d = _UnityCameraForwardScale > 0.0 ? _UnityCameraForwardScale * d : d;
                float zBufferParamsW = 1.0 / _ProjectionParams.y;
                float zBufferParamsY = _ProjectionParams.z * zBufferParamsW;
                float zBufferParamsX = 1.0 - zBufferParamsY;
                float zBufferParamsZ = zBufferParamsX * _ProjectionParams.w;
                return (d < _ProjectionParams.y) ? 1.0 : ((1.0 / zBufferParamsZ) * ((1.0 / d) - zBufferParamsW));
            }

            void main()
            {
#ifdef SHADER_API_GLES3
                float cx = textureCoord.x * _CropScaleX + _CropOffsetX;
                if (cx < 0.0 || cx > 1.0) { gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0); gl_FragDepth = 1.0; return; }
                vec2 tc = vec2(cx, textureCoord.y);
                vec3 result = texture(_MainTex, tc).xyz;
#ifndef UNITY_COLORSPACE_GAMMA
                result = GammaToLinearSpace(result);
#endif
                gl_FragColor = vec4(result, 1.0);
                gl_FragDepth = 1.0;
#endif
            }
#endif
            ENDGLSL
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SubShader 2: Android Vulkan (HLSL, sampler2D)
    // ─────────────────────────────────────────────────────────────────────────
    SubShader
    {
        Name "ARCamera LateralCrop Vulkan"
        Tags { "Queue"="Background" "RenderType"="Background" "ForceNoShadowCasting"="True" }

        Pass
        {
            Cull Off ZTest Always ZWrite On Lighting Off
            Tags { "LightMode"="Always" }

            HLSLPROGRAM
            #pragma only_renderers vulkan
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4x4 _UnityDisplayTransform;
            float    _CropOffsetX;
            float    _CropScaleX;

            struct appdata { float4 vertex : POSITION; float3 uv : TEXCOORD0; };
            struct v2f     { float4 position : SV_POSITION; float2 texcoord : TEXCOORD0; };

            v2f vert(appdata i)
            {
                v2f o;
                o.position = UnityObjectToClipPos(i.vertex.xyz);
                o.texcoord = mul(float4(i.uv.x, i.uv.y, 1.0f, 0.0f), _UnityDisplayTransform).xy;
                return o;
            }

            sampler2D _MainTex;
            float     _UnityCameraForwardScale;

#ifndef UNITY_COLORSPACE_GAMMA
            float3 GammaToLinearSpace(float3 sRGB)
            {
                return sRGB * (sRGB * (sRGB * 0.305306011F + 0.682171111F) + 0.012522878F);
            }
#endif
            float ConvertDistanceToDepth(float d)
            {
                d = _UnityCameraForwardScale > 0.0 ? _UnityCameraForwardScale * d : d;
                float zBufferParamsW = 1.0 / _ProjectionParams.y;
                float zBufferParamsY = _ProjectionParams.z * zBufferParamsW;
                float zBufferParamsX = 1.0 - zBufferParamsY;
                float zBufferParamsZ = zBufferParamsX * _ProjectionParams.w;
                return (d < _ProjectionParams.y) ? 1.0f : ((1.0 / zBufferParamsZ) * ((1.0 / d) - zBufferParamsW));
            }

            struct fragOutput { float4 color : SV_Target; float depth : SV_Depth; };

            fragOutput frag(v2f i)
            {
                fragOutput o;
                float cx = i.texcoord.x * _CropScaleX + _CropOffsetX;
                if (cx < 0.0 || cx > 1.0) { o.color = float4(0,0,0,1); o.depth = 1.0; return o; }
                float2 tc = float2(cx, i.texcoord.y);
                float3 result = tex2D(_MainTex, tc).xyz;
#ifndef UNITY_COLORSPACE_GAMMA
                result = GammaToLinearSpace(result);
#endif
                o.color = float4(result, 1.0);
                o.depth = 1.0 - 1.0; // Vulkan reverse-Z, background → depth 0
                return o;
            }
            ENDHLSL
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SubShader 3: iOS ARKit (YCbCr)
    // ─────────────────────────────────────────────────────────────────────────
    SubShader
    {
        Name "ARCamera LateralCrop ARKit"
        Tags { "Queue"="Background" "RenderType"="Background" "ForceNoShadowCasting"="True" }

        Pass
        {
            Cull Off ZTest Always ZWrite On Lighting Off
            Tags { "LightMode"="Always" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define real4    half4
            #define real4x4  half4x4
            #define TransformObjectToHClip UnityObjectToClipPos
            #define FastSRGBToLinear       GammaToLinearSpace
            #define ARKIT_TEXTURE2D_HALF(tex)           UNITY_DECLARE_TEX2D_HALF(tex)
            #define ARKIT_SAMPLER_HALF(s)
            #define ARKIT_SAMPLE_TEXTURE2D(tex,s,uv)    UNITY_SAMPLE_TEX2D(tex,uv)

            CBUFFER_START(UnityARFoundationPerFrame)
                float4x4 _UnityDisplayTransform;
                float    _UnityCameraForwardScale;
            CBUFFER_END

            float _CropOffsetX;
            float _CropScaleX;

            struct appdata { float3 position : POSITION; float2 texcoord : TEXCOORD0; };
            struct v2f     { float4 position : SV_POSITION; float2 texcoord : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = TransformObjectToHClip(v.position);
                o.texcoord = mul(float4(v.texcoord, 1.0f, 1.0f), _UnityDisplayTransform).xy;
                return o;
            }

            CBUFFER_START(ARKitColorTransformations)
                static const real4x4 s_YCbCrToSRGB = real4x4(
                    real4(1.0h,  0.0000h,  1.4020h, -0.7010h),
                    real4(1.0h, -0.3441h, -0.7141h,  0.5291h),
                    real4(1.0h,  1.7720h,  0.0000h, -0.8860h),
                    real4(0.0h,  0.0000h,  0.0000h,  1.0000h));
            CBUFFER_END

            inline float ConvertDistanceToDepth(float d)
            {
                d = _UnityCameraForwardScale > 0.0 ? _UnityCameraForwardScale * d : d;
                return (d < _ProjectionParams.y) ? 0.0f : ((1.0f / _ZBufferParams.z) * ((1.0f / d) - _ZBufferParams.w));
            }

            ARKIT_TEXTURE2D_HALF(_textureY);
            ARKIT_SAMPLER_HALF(sampler_textureY);
            ARKIT_TEXTURE2D_HALF(_textureCbCr);
            ARKIT_SAMPLER_HALF(sampler_textureCbCr);

            struct fragment_output { real4 color : SV_Target; float depth : SV_Depth; };

            fragment_output frag(v2f i)
            {
                fragment_output o;
                float cx = i.texcoord.x * _CropScaleX + _CropOffsetX;
                if (cx < 0.0 || cx > 1.0) { o.color = real4(0,0,0,1); o.depth = 1.0; return o; }
                float2 tc = float2(cx, i.texcoord.y);

                real4 ycbcr = real4(
                    ARKIT_SAMPLE_TEXTURE2D(_textureY,    sampler_textureY,    tc).r,
                    ARKIT_SAMPLE_TEXTURE2D(_textureCbCr, sampler_textureCbCr, tc).rg,
                    1.0h);
                real4 videoColor = mul(s_YCbCrToSRGB, ycbcr);

#if !UNITY_COLORSPACE_GAMMA
                videoColor.xyz = FastSRGBToLinear(videoColor.xyz);
#endif
                o.color = videoColor;
                o.depth = 0.0f;
                return o;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
