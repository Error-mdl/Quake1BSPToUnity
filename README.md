# Quake1BSPToUnity
Import Quake 1 .bsp map files into Unity with shader support for texture arrays and lightmaps
# Usage
Open the importer window under Window/Q1 BSP Import, Select the BSP to import as well as the locations of the Quake 1 brush shader (textureArrayBasic) and the Quake Colormap and Lightstyle textures. Click "Create Mesh and Textures" and select a folder in which to dump all of the meshes, textures, and materials. The assembled map prefab can then be found in that folder under the name \_(Name of the Original BSP file)\_Prefab.prefab"
# About
As Quake was originally desgined around its software renderer, many of the design decisions don't mesh well with rendering on a GPU. In particular, Quake maps use a large number of small tiling textures and up to 4 individual lightmap textures per face. With hardware acceleration having each face use different textures would require a separate draw call for each one, which would completely bog down the CPU. The normal solution to this is to atlas the texure information of each face into a single large texture. This works well for textures that don't repeat, like lightmaps, but for the normal color textures which tile extensively and are used on many surfaces that means most of the atlas is redundant information. A more elegant solution in this case is to use texture arrays, which are arrays of textures of the same dimensions and format which can be passed to the gpu in a single draw call. It is then up to the shader to determine which texture in the array to sample based off of information stored in each vertex of the mesh.

This script imports Quake 1 .bsp maps in conjunction with a shader designed to mimic the look of the original software renderer. The importer generates texture arrays from the textures in the BSP, binning textures of the same dimensions into separate arrays, and generates a material for each array. It then generates each mesh in the BSP, storing information about each face in the UV channels and vertex colors. The base UVs are stored in the first UV channel and the lightmap UVs in the second channel. In the third channel it stores the dimensions of the face's lightmap, the index in the array of bytes containing all lightmaps where the face's lightmap begins, and the index to use for the texture array. It then generates a prefab containing all meshes with properly assigned materials.

The included shader made for Unity's bulit-in render pipeline is the only one that will work with the generated mesh, lightmaps, and texture arrays. If you wish to use a different shader, then you will need to modify it to support texture arrays and use the information stored in the mesh. The textures are all of 8 bit R format, only storing an index into the Quake color palette, and the shader reads from the colormap to determine the actual diffuse color. The index of the texture in the array to use is stored in the fourth component of TEXCOORD2 as a float. The binary data of this float is actually an int, and treating it as an int will produce the correct array index. Because I'm too dumb and lazy to figure out how to write a UV packer, I don't actually atlas the lightmap and instead just dump the contiguous array of bytes of all lightmaps into a single texture. The shader then takes the lightmap UV's, dimensions of the faces lightmap from the xy channels of TEXCOORD2, and the index in the array of bytes where the lightmap begins stored in the 3rd component of TEXCOORD2 also as an int encoded in a float and determines the 1d index in the array of bytes to read from. It then takes the dimensions of the texture containing the array of bytes and finds the 2d UV coordinates of that byte and reads it from the texture. Bilinear filtering is done manually, sampling the texture four times. This may be done up to four times, depending on how many lightmaps the face has. Suprisingly, this seems to not impact performance measurably.
