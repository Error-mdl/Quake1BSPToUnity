Shader "Quake/Brush Texture Array"
{
  Properties
  {
      [HideInInspector] _MainTex("Fake Texture", 2D) = "white" {}
      _PaletteTex("Palette Texture", 2D) = "white" {}
      _StyleTex("Light Style Texture", 2D) = "white" {}
      _LMTex("Lightmap Texture", 2D) = "white" {}
      _MainTexArray ("Texture", 2DArray) = "white" {}
      _LightFlags("Light Flags", Int) = 0
      [ToggleUI] _FullBright("Fullbright", Int) = 0
  }
  SubShader
  {
    Tags {"Rendertype"="Opaque" "Queue"="Geometry"}
   
    Pass
    {
        Name "META"
        Tags {
            "LightMode" = "Meta"
        }
        Cull Off
        //Cull Back
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        // make fog work
        //#pragma multi_compile_fog

        #include "UnityCG.cginc"
        #include "UnityMetaPass.cginc"

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            float2 uv_lightmap : TEXCOORD1;
            float4 color : COLOR;
        };

        struct v2f
        {
            float3 uv : TEXCOORD0;
            float2 uv_lightmap : TEXCOORD1;
            //float4 settings : TEXCOORD2;
            float4 vertex : SV_POSITION;
        };

        //sampler2D _MainTex;
        Texture2D<half4> _PaletteTex;
        float4 _PaletteTex_TexelSize;
        UNITY_DECLARE_TEX2DARRAY(_MainTexArray);
        Texture2D<half4> _SettingsTex;

        v2f vert(appdata v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            float index = floor(floor(v.color.r * 10.0 + 0.001) + 10 * floor(v.color.g * 10.0 + 0.0001));
            o.uv.xy = v.uv;
            o.uv.z = index;
            o.uv_lightmap = v.uv_lightmap;
            //o.settings = _SettingsTex.Load(float4(index, 0, 0, 0));
            //if (o.settings.g > 0.9)
            //{
            //    o.vertex = float4 (0, 0, 0, 1);
            //}
            return o;
        }

        float4 frag(v2f i) : SV_Target
        {
            // sample the texture
            UnityMetaInput surfaceData;
            int paletteIndex = floor(255 * UNITY_SAMPLE_TEX2DARRAY(_MainTexArray, i.uv).r + 0.01);
            float4 col = _PaletteTex.Load(int3(paletteIndex, 32, 0), int2(0, 0));
            //clip((col.a - 0.5)* i.settings.g);
            surfaceData.Albedo = saturate(col.rgb*4);
            surfaceData.Emission = 0;//10.0 * col.rgb * i.settings.r * col.a;
            surfaceData.SpecularColor = float3(0.5, 0.5, 0.5);

            return UnityMetaFragment(surfaceData);
        }
        ENDCG
    }
    
    Pass
    {
       Tags {
          "LightMode" = "Forwardbase"
      }
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      // make fog work
      //#pragma multi_compile_fog
      #pragma multi_compile _ LIGHTMAP_ON
      
      #include "AutoLight.cginc"
      #include "Lighting.cginc"
      #include "UnityCG.cginc"

      struct appdata
      {
          float4 vertex : POSITION;
          float2 uv : TEXCOORD0;
          float2 uv_lightmap : TEXCOORD1;
          float4 lightmap_dim : TEXCOORD2;
          float4 color : COLOR;
          float3 normal : NORMAL;
      };

      struct v2f
      {
          float3 uv : TEXCOORD0;
          float4 uv_lightmap : TEXCOORD1;
          //float2 lightmap_dim : TEXCOORD2;
          int2 lmOffset_mipLevel : TEXCOORD2;
          float4 lightStyles : TEXCOORD3;
          float3 normal : NORMAL;
          float4 vertex : SV_POSITION;
      };

      UNITY_DECLARE_TEX2DARRAY(_MainTexArray);
      UNITY_DECLARE_TEX2D(_PaletteTex);
      UNITY_DECLARE_TEX2D(_LMTex);
      UNITY_DECLARE_TEX2D(_StyleTex);
      //Texture2D _LMTex;
      float4 _LMTex_TexelSize;
      float4 _StyleTex_TexelSize;
      float4 _PaletteTex_TexelSize;
      sampler2D unity_LightMap;
      float4 unity_LightMapST;  
      int _LightFlags;
      int _FullBright;

      v2f vert (appdata v)
      {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        float index = floor(v.color.r * 255.0 + 0.001);
        o.uv.xy = v.uv;
        o.uv.z = asint(v.lightmap_dim.w);
      
            //o.lightmap_dim = v.lightmap_dim.xy;
            o.uv_lightmap = float4(v.uv_lightmap, v.lightmap_dim.xy);// *unity_LightmapST.xy + unity_LightmapST.zw;
     
        o.normal = UnityObjectToWorldNormal(v.normal);
        //o.uv_lightmap2 = v.uv_lightmap2;
       
        o.lmOffset_mipLevel.x = asint(v.lightmap_dim.z);
        float4 camPos = mul(UNITY_MATRIX_MV, v.vertex);
        o.lmOffset_mipLevel.y = clamp(0, 3, floor(abs(camPos.z) / 20));
        o.lightStyles = v.color;
        return o;
      }

      int2 rewindUVs(int2 uv, const float2 lmDim, const float2 lmTexDim, const int lmOffset)
      {
          int unravel_LmUv = uv.x + (uv.y * (int)(lmDim.x + 1.0001)) + lmOffset;
          int2 rewind_LmUv = int2(unravel_LmUv % lmTexDim.x, (unravel_LmUv / lmTexDim.x));
          return rewind_LmUv;
      }

      float read_lm(float2 uv, const float2 lmDim, const float2 lmTexDim, const int lmOffset, const float lmStyle)
      {
          int x = frac(uv.x) > 0.5 ? 1 : -1;
          int y = frac(uv.y) > 0.5 ? 1 : -1;
         // float2 uvX = float2(clamp(uv.x + x, 0, lmDim.x), uv.y);
         // float2 uvY = float2(uv.x, clamp(uv.y + y, 0, lmDim.y));
         // float2 uvXY = float2(uvX.x, uvY.y);
          
          int2 floorUVs = int2(uv);
          int2 floorUVsX = int2(uv) + int2(x,0);
          int2 floorUVsY = int2(uv) + int2(0,y);
          int2 floorUVsXY = int2(uv) + int2(x,y);

          
          int2 rewind_LmUv = rewindUVs(floorUVs, lmDim, lmTexDim, lmOffset);
          int2 rewind_LmUvX = rewindUVs(floorUVsX, lmDim, lmTexDim, lmOffset);
          int2 rewind_LmUvY = rewindUVs(floorUVsY, lmDim, lmTexDim, lmOffset);
          int2 rewind_LmUvXY = rewindUVs(floorUVsXY, lmDim, lmTexDim, lmOffset);
          float lm = _LMTex.Load(int3(rewind_LmUv, 0)).r;
          float lmX = _LMTex.Load(int3(rewind_LmUvX, 0)).r;
          float lmY = _LMTex.Load(int3(rewind_LmUvY, 0)).r;
          float lmXY = _LMTex.Load(int3(rewind_LmUvXY, 0)).r;

          lm = lerp(lm, lmX, abs(0.5 - frac(uv.x)));
          lmY = lerp(lmY, lmXY, abs(0.5 - frac(uv.x)));
          lm = lerp(lm, lmY, abs(0.5 - frac(uv.y)));

          int styleTime = fmod(_Time[2] * 5.0, _StyleTex_TexelSize.z);
          int lmStyleInt = 255 * lmStyle + 0.001;
          int lmStyleFlag = (1 << (lmStyleInt - 32));
          int lmStyleInt2 = lmStyleInt < 32 ? lmStyleInt : 0;
          float style = 22 * _StyleTex.Load(int3(styleTime, lmStyleInt2, 0)).r;
          style *= lmStyleInt > 31 ? (lmStyleFlag & _LightFlags) == lmStyleFlag : 1;
          return lm * style;
      }

      float4 frag(v2f i) : SV_Target
      {
        int paletteIndex = floor(255*UNITY_SAMPLE_TEX2DARRAY_LOD(_MainTexArray, i.uv, i.lmOffset_mipLevel.y).r + 0.01);
        float3 normal = normalize(i.normal);
        half nl = saturate(dot(_WorldSpaceLightPos0.xyz, i.normal));
        float3 dir_light = nl * _LightColor0.rgb;

        float3 baked_light = max(0,ShadeSH9(float4(i.normal, 1)));

        float4 lm = float4(0, 0, 0, 0);
        #if defined(LIGHTMAP_ON)
        lm = UNITY_SAMPLE_TEX2D(unity_Lightmap, i.uv_lightmap);
        lm.rgb = DecodeLightmap(lm);
        #endif
        float test = 1.0;
       
        int lightmap_offset = i.lmOffset_mipLevel.x;
        float lm2 = 0;
        if (i.lightStyles.r < 0.999999)
        {
            lm2 += read_lm(i.uv_lightmap.xy, i.uv_lightmap.zw, _LMTex_TexelSize.zw, lightmap_offset, i.lightStyles.r);
        }
        if (i.lightStyles.g < 0.999999)
        {
            lm2 += read_lm(i.uv_lightmap.xy, i.uv_lightmap.zw, _LMTex_TexelSize.zw, lightmap_offset + int((i.uv_lightmap.z + 1.00001) * (i.uv_lightmap.w + 1.00001)), i.lightStyles.g);
        }
        if (i.lightStyles.b < 0.999999)
        {
            lm2 += read_lm(i.uv_lightmap.xy, i.uv_lightmap.zw, _LMTex_TexelSize.zw, lightmap_offset + 2 * int((i.uv_lightmap.z + 1.00001) * (i.uv_lightmap.w + 1.00001)), i.lightStyles.b);
        }
        if (i.lightStyles.a < 0.999999)
        {
            lm2 += read_lm(i.uv_lightmap.xy, i.uv_lightmap.zw, _LMTex_TexelSize.zw, lightmap_offset + 3 * int((i.uv_lightmap.z + 1.00001) * (i.uv_lightmap.w + 1.00001)), i.lightStyles.a);
        }
        lm2 = _FullBright > 0 ? 0.53 : lm2;

        //Colormap contains 0 to +2 brightness colors for each color in the color palette, with pixels at 32 being 1 brightness
        int value = floor(63 * (lm2));
        value = clamp(0, 63, value);

        float4 col = _PaletteTex.Load(int3(paletteIndex, value, 0));
        //col = col;
        
        //return  i.lightmap_offset == 36228 ? float4(unravel_LmUv - i.lightmap_offset,0,0, 1) : float4(0.25,0,0,1);
        //return float4(unravel_LmUv,0,0,1);
        //return lm2;
        return col;
        
      }
      ENDCG
    }
  }
}
