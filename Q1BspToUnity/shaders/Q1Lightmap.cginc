/** @file Q1Lightmap.cginc
 *  
 *  @brief Contains a number of functions pertaining to reading the Quake 1 BSP lightmap information
 *         from a dump of the BSP's lightmap byte array stored in a R8 texture.
 *
 *  Each face in a BSP uses its own lightmap textures, and an index is stored with the face which
 *  points to the beginning of the face's lightmap in the lightmap byte array. The face then treats 
 *  the next (face lightmap width) * (face lightmap height) bytes as a texture of those dimensions.
 *  Each face can use up to 4 lightmaps, which are stored sequentially in the lightmap byte array.
 *  These 4 lightmaps are added together to get the final light level. Additionally, each lightmap
 *  has its own "light style" which can make it flicker/flash/strobe or be toggled on/off by a
 *  button. The light styles were originally strings stored in "world.qc" which contain the
 *  characters a-z with 'a' representing no light, 'm' representing close to 1 brightness, and
 *  'z' representing 2 brightness, and these would be iterated over at a rate of 10 characters
 *  per second. These strings have been converted into a texture in order to read them with this
 *  shader. The "light style" stored for each face is an 8 bit int which if it is less than 32
 *  indicates which light style string the lightmap should be multiplied by, or if it is between
 *  32 and 63 it indicates a button can be used to turn on/off the light. In order to handle
 *  this functionality, this shader uses a 32 bit int to control which lights are on/off with
 *  each bit of the int acting as a flag for if that light is on or off. Additionally if the
 *  style int is 255, then the lightmap is empty and nothing is added for that lightmap.
 *  
 *  @author Error.mdl
 */

/** @brief Converts the lightmap uvs into a 1d array index and then back into a coordinate in the lightmap lump texture
 *
 *  Takes the original lightmap uvs and "unwraps" them into a 1d array index using the lightmap dimensions + 1 on
 *  each axis (+1 because the lightmaps are actually 1 pixel larger on each axis than the face bounding box). It
 *  then adds the lightmap array offset to this index. Finally, it takes the dimensions for our lightmap lump
 *  texture, and "rewinds" the 1d index around the texture to get the 2d coordinate of the pixel in the lightmap
 *  lump texture.
 *
 *  @param uv The lightmap uv coordinate
 *  @param lmDim The dimensions of the 2d face bounding box / 16, which represents the dimensions of the face's lightmap -1 on each axis
 *  @param lmTexDim The dimensions of the texture containing the entire lightmap lump
 *  @param lmOffset The offset into the byte array of the lightmap lump where the face's lightmap begins
 * 
 *  @return UV coordinates of the byte in the lightmap lump texture corresponding to the lightmap uv and lightmap offset
 */

int2 rewindUVs(int2 uv, const float2 lmDim, const float2 lmTexDim, const int lmOffset)
{
    int unravel_LmUv = uv.x + (uv.y * (int)(lmDim.x + 1.0001)) + lmOffset;
    int2 rewind_LmUv = int2(unravel_LmUv % lmTexDim.x, (unravel_LmUv / lmTexDim.x));
    return rewind_LmUv;
}


/** @brief Reads the lightmap, does bilinear filtering, and applies the light styles
 *
 *  Reads the lightmap, finding the correct pixel in the texure containing the array bytes of the BSP's lightmap lump.
 *  Since adjacent pixels in this texture aren't actually spatially related, we have to manually do bilinear sampling
 *  ourselves by taking the 4 closest integer coordinates to the lightmap uvs, converting them into coordinates in the 
 *  lightmap lump texture and sampling it, and blending between the four values using bilinear blending. Additionally,
 *  calculate the multiplier for the light style if the style is less than 31, or if it is greater than 31 set the light
 *  style multiplier to either 1 or 0 if the bit for that light style is set in the LightFlag int.
 *  
 *  @param LightmapTex The lightmap lump texture
 *  @param LightStyleTex The light style strings converted to a texture
 *  @param uv The lightmap uvs
 *  @param lmDim The dimensions of the 2d face bounding box / 16, which represents the dimensions of the face's lightmap -1 on each axis
 *  @param lmTexDim The dimensions of the texture containing the entire lightmap lump
 *  @param lsTexDim The dimensions of the light style texture
 *  @param lmOffset The offset into the byte array of the lightmap lump where the face's lightmap begins
 *  @param lmStyle The number of the light style the face uses
 *  @param lightFlag A 32 bit int where each bit represents the state of the 32 possible toggleable lights represented by light styles 32-63
 *
 *  @return The lightmap value
 */
float read_lm_bilinear(uniform const Texture2D LightmapTex, uniform const Texture2D LightStyleTex, float2 uv, const float2 lmDim, const float2 lmTexDim, const float2 lsTexDim, const int lmOffset, const float lmStyle, const int lightFlag)
{
    int x = frac(uv.x) > 0.5 ? 1 : -1;
    int y = frac(uv.y) > 0.5 ? 1 : -1;

    int2 floorUVs = int2(uv);
    int2 floorUVsX = int2(uv)+int2(x, 0);
    int2 floorUVsY = int2(uv)+int2(0, y);
    int2 floorUVsXY = int2(uv)+int2(x, y);


    int2 rewind_LmUv = rewindUVs(floorUVs, lmDim, lmTexDim, lmOffset);
    int2 rewind_LmUvX = rewindUVs(floorUVsX, lmDim, lmTexDim, lmOffset);
    int2 rewind_LmUvY = rewindUVs(floorUVsY, lmDim, lmTexDim, lmOffset);
    int2 rewind_LmUvXY = rewindUVs(floorUVsXY, lmDim, lmTexDim, lmOffset);
    float lm = LightmapTex.Load(int3(rewind_LmUv, 0)).r;
    float lmX = LightmapTex.Load(int3(rewind_LmUvX, 0)).r;
    float lmY = LightmapTex.Load(int3(rewind_LmUvY, 0)).r;
    float lmXY = LightmapTex.Load(int3(rewind_LmUvXY, 0)).r;

    lm = lerp(lm, lmX, abs(0.5 - frac(uv.x)));
    lmY = lerp(lmY, lmXY, abs(0.5 - frac(uv.x)));
    lm = lerp(lm, lmY, abs(0.5 - frac(uv.y)));

    int styleTime = fmod(_Time[2] * 5.0, lsTexDim.x);
    int lmStyleInt = 255 * lmStyle + 0.001;
    int lmStyleFlag = (1 << (lmStyleInt - 32));
    int lmStyleInt2 = lmStyleInt < 32 ? lmStyleInt : 0;
    float style = 22 * LightStyleTex.Load(int3(styleTime, lmStyleInt2, 0)).r;
    style *= lmStyleInt > 31 ? (lmStyleFlag & lightFlag) == lmStyleFlag : 1;
    return lm *style;
}


/** @brief Read all four face lightmaps, apply the appropriate light styles, and add them into a single float ranging from 0 to 2 
 *
 *  @param LightmapTex The lightmap lump texture
 *  @param LightStyleTex The light style strings converted to a texture
 *  @param uv_lightmap The lightmap coordinates in the face's lightmap
 *  @param faceLightmapDim The dimensions of the 2d face bounding box / 16, which represents the dimensions of the face's lightmap -1 on each axis
 *  @param lightmapTextureDim The dimensions of the texture containing the entire lightmap lump
 *  @param lightstyleTextureDim The dimensions of the light style texture
 *  @param lightmapOffset The offset into the byte array of the lightmap lump where the face's lightmap begins
 *  @param lightStyles The four light styles corresponding to each of the four possible lightmaps
 *  @param lightFlag A 32 bit int where each bit represents the state of the 32 possible toggleable lights represented by light styles 32-63
 *  @param fullbright Flag that gets set if the converted map has no lightmaps. If true, just set the lightmap value to about 1
 *
 *  @return The sum of all four lightmap values
 */
float decodeQ1Lightmap(uniform const Texture2D LightmapTex, uniform const Texture2D LightStyleTex, float2 uv_lightmap, const float2 faceLightmapDim,
                        const float2 lightmapTextureDim, const float2 lightstyleTextureDim, const int lightmapOffset, const float4 lightStyles, const int lightFlag, const int fullbright)
{
    float lm = 0;

    // Each lightmap is stored sequentially, so add a multiple of the product of the lightmap dimensions to the lightmap offset to find each lightmap
    int subLightmapOffset = int((faceLightmapDim.x + 1.00001) * (faceLightmapDim.y + 1.00001));

    // light styles is normally a char, but it is stored in vertex colors so it is read in the shader as a 0-1 float. A value of 1 now represents no lighmap. 
    if (lightStyles.r < 0.999999)
    {
        lm += read_lm_bilinear(LightmapTex, LightStyleTex, uv_lightmap, faceLightmapDim, lightmapTextureDim, lightstyleTextureDim, lightmapOffset, lightStyles.r, lightFlag);
    }
    if (lightStyles.g < 0.999999)
    {
        lm += read_lm_bilinear(LightmapTex, LightStyleTex, uv_lightmap, faceLightmapDim, lightmapTextureDim, lightstyleTextureDim, lightmapOffset + subLightmapOffset, lightStyles.g, lightFlag);
    }
    if (lightStyles.b < 0.999999)
    {
        lm += read_lm_bilinear(LightmapTex, LightStyleTex, uv_lightmap, faceLightmapDim, lightmapTextureDim, lightstyleTextureDim, lightmapOffset + 2 * subLightmapOffset, lightStyles.b, lightFlag);
    }
    if (lightStyles.a < 0.999999)
    {
        lm += read_lm_bilinear(LightmapTex, LightStyleTex, uv_lightmap, faceLightmapDim, lightmapTextureDim, lightstyleTextureDim, lightmapOffset + 3 * subLightmapOffset, lightStyles.a, lightFlag);
    }

    // note that in the light styles, 'm' is supposed to be normal at 0.5 brightness, but the math to calculate the multiplier from the character actually gives 0.52
    lm = fullbright > 0 ? 0.52 : lm; 
    // rescale so the brightness ranges from 0 to 2
    lm *= 2;

    return lm;
}
