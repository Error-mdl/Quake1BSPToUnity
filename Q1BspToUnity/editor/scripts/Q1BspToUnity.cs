using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.IO;
using UnityEngine;
using UnityEditor;

public class Q1BspToUnity : EditorWindow
{
  public string bspPath;
  public Shader texArrayShader;
  public Texture2D colorPalette;
  public Texture2D lightStyles;

  [StructLayout(LayoutKind.Sequential)]
  public struct EdgeInfo
  {
    public UInt16 vertex0;
    public UInt16 vertex1;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct FaceInfo
  {
    public Int16 planeId;
    public Int16 side;
    public int ledge_id;
    public Int16 ledge_num;
    public Int16 texInfo_id;

    public char light0;
    public char light1;
    public char light2;
    public char light3;

    public int lightMap_id;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct PlaneInfo
  {
    public float normalX, normalY, normalZ;
    public float dist;
    public int type;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct modelInfo
  {
    public float bboxMinX, bboxMinY, bboxMinZ;
    public float bboxMaxX, bboxMaxY, bboxMaxZ;
    public float originX, originY, originZ;
    public int node_id0, node_id1, node_id2, node_id3;
    public int numleafs;
    public int face_id;
    public int face_num;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct MaterialUV
  {
    public float Sx, Sy, Sz;
    public float SOffset;
    public float Tx, Ty, Tz;
    public float TOffset;
    public int texture_id;
    public int isAnimated;
  }


  public struct TextureInfo
  {
    public string Name;
    public int width;
    public int height;
    public uint offset_header;
    public uint offset_mip0;
    public uint offset_mip1;
    public uint offset_mip2;
    public uint offset_mip3;

    // there's no way the texture width/height would fill even a 16 bit uint,
    // so shift the height 16 bits up and OR the width and height to make a "hash"
    // of the dimensions that we can use to easily compare to other textures
    public int dimHash { get => this.width | (this.height << 16); }
    public int originalIndex;
    public int materialIndex;
    public int arrayIndex;
  }

  // Method for directly copying an array of bytes into a struct. Copypasta from stackoverflow.
  public static T RawDataToStruct<T>(byte[] rawData) where T : struct
  {
    var pinnedRawData = GCHandle.Alloc(rawData, GCHandleType.Pinned);
    try
    {
      var pinnedRawDataPtr = pinnedRawData.AddrOfPinnedObject();
      return (T)Marshal.PtrToStructure(pinnedRawDataPtr, typeof(T));
    }
    finally
    {
      pinnedRawData.Free();
    }
  }
  private void OnEnable()
  {
    GetWindow<Q1BspToUnity>().texArrayShader = Shader.Find("Quake/Brush Texture Array");
    GetWindow<Q1BspToUnity>().colorPalette = (Texture2D)AssetDatabase.LoadAssetAtPath("Assets/Q1BspToUnity/textures/colormap.png", typeof(Texture2D));
    GetWindow<Q1BspToUnity>().lightStyles = (Texture2D)AssetDatabase.LoadAssetAtPath("Assets/Q1BspToUnity/textures/quake_lightstyles.png", typeof(Texture2D));
  }
  static void Init()
  {
    const int width = 400;
    const int height = 220;

    var x = (Screen.currentResolution.width - width) / 2;
    var y = (Screen.currentResolution.height - height) / 2;

    GetWindow<Q1BspToUnity>().position = new Rect(x, y, width, height);
    
  }

  [MenuItem("Window/Q1 BSP Import")]
  public static void ShowWindow()
  {
    GetWindow<Q1BspToUnity>(true, "Import BSP Files", true);
  }
  void OnGUI()
  {
    EditorGUILayout.BeginHorizontal();
    if (GUILayout.Button("Select BSP Path", GUILayout.Width(110), GUILayout.Height(20)))
    {
      bspPath = EditorUtility.OpenFilePanel("Select BSP file", "Assets/", "bsp");
    }
    EditorGUILayout.TextField(bspPath);
    EditorGUILayout.EndHorizontal();
    texArrayShader = EditorGUILayout.ObjectField("Shader", texArrayShader, typeof(Shader), false) as Shader;
    colorPalette = EditorGUILayout.ObjectField("Color Palette", colorPalette, typeof(Texture2D), false) as Texture2D;
    lightStyles = EditorGUILayout.ObjectField("Light Style Texture", lightStyles, typeof(Texture2D), false) as Texture2D;
    EditorGUILayout.BeginHorizontal();
    GUILayout.FlexibleSpace();
    if (GUILayout.Button("Create Mesh and Textures", GUILayout.Width(175), GUILayout.Height(30)))
    {
      ReadBSP();
    }
  }

  // finds the smallest power of 2 greater than a given number
  private int RoundUpToPow2(int num)
  {
    if (num < 0) { return 0; }
    --num;
    num |= num >> 1;
    num |= num >> 2;
    num |= num >> 4;
    num |= num >> 8;
    num |= num >> 16;
    return num + 1;
  }

  private Vector2 UV_from_ST(Vector3 vertex, MaterialUV matUV, TextureInfo texInfo)
  {
    Vector3 S = new Vector3(matUV.Sx, matUV.Sy, matUV.Sz);
    Vector3 T = new Vector3(matUV.Tx, matUV.Ty, matUV.Tz);
    Vector2 vtxUV = new Vector2(Vector3.Dot(vertex, S) + matUV.SOffset, Vector3.Dot(vertex, T) + matUV.TOffset);
    vtxUV *= new Vector2(1.0f / (float)texInfo.width, -1.0f / (float)texInfo.height);
    return vtxUV;
  }

  private Vector2 LM_UV_from_ST(Vector3 vertex, MaterialUV matUV)
  {
    Vector3 S = new Vector3(matUV.Sx, matUV.Sy, matUV.Sz);
    Vector3 T = new Vector3(matUV.Tx, matUV.Ty, matUV.Tz);
    Vector2 vtxUV = new Vector2(Vector3.Dot(vertex, S), Vector3.Dot(vertex, T));
    return vtxUV;
  }

  void ReadBSP()
  {
    string rawPath = EditorUtility.SaveFolderPanel("Save map and textures", "Assets/", Path.GetFileNameWithoutExtension(bspPath));
    string path = "Assets" + rawPath.Substring(Application.dataPath.Length);

    Stream s = new FileStream(bspPath, FileMode.Open);
    BinaryReader br = new BinaryReader(s);

    // Read the header
    int bspVersion = br.ReadInt32();
    int offset_entity = br.ReadInt32();
    int length_entity = br.ReadInt32();
    int offset_planes = br.ReadInt32();
    int length_planes = br.ReadInt32();
    int textureDirectoryOffset = br.ReadInt32();
    //Debug.Log(String.Format("Texture Directory Offset, {0}", textureDirectoryOffset));
    int textureDirectorySize = br.ReadInt32();
    int offset_vertex = br.ReadInt32();
    int length_vertex = br.ReadInt32();
    int offset_vislist = br.ReadInt32();
    int length_vislist = br.ReadInt32();
    int offset_nodes = br.ReadInt32();
    int length_nodes = br.ReadInt32();
    int offset_texinfo = br.ReadInt32();
    int length_texinfo = br.ReadInt32();
    int offset_faces = br.ReadInt32();
    int length_faces = br.ReadInt32();
    int offset_lightmaps = br.ReadInt32();
    int length_lightmaps = br.ReadInt32();
    int offset_clipnodes = br.ReadInt32();
    int length_clipnodes = br.ReadInt32();
    int offset_leaves = br.ReadInt32();
    int length_leaves = br.ReadInt32();
    int offset_lface = br.ReadInt32();
    int length_lface = br.ReadInt32();
    int offset_edge = br.ReadInt32();
    int length_edge = br.ReadInt32();
    int offset_ledge = br.ReadInt32();
    int length_ledge = br.ReadInt32();
    int offset_model = br.ReadInt32();
    int length_model = br.ReadInt32();


    //Debug.Log(String.Format("Model Offset, {0}", offset_model));

    //Read the texture directory to find the number and offsets of the textures
    br.BaseStream.Seek(textureDirectoryOffset, SeekOrigin.Begin);
    int numTextures = br.ReadInt32();
    Debug.Log(String.Format("Number of Textures, {0}", numTextures));
    int[] textureOffsets = new int[numTextures];
    for (int i = 0; i < numTextures; i++)
    {
      textureOffsets[i] = br.ReadInt32();
    }

    //Read each texture's header to find the texture name, dimensions, and offsets of each of the 4 mip levels
    TextureInfo[] textureInfo = new TextureInfo[numTextures];
    for (int i = 0; i < numTextures; i++)
    {
      br.BaseStream.Seek(textureDirectoryOffset + textureOffsets[i], SeekOrigin.Begin);
      textureInfo[i].Name = String.Join("", new string(br.ReadChars(16)).Substring(0, 12).Split('\0'));
      textureInfo[i].width = (int)br.ReadUInt32();
      textureInfo[i].height = (int)br.ReadUInt32();
      textureInfo[i].offset_header = (uint)textureOffsets[i];
      textureInfo[i].offset_mip0 = br.ReadUInt32();
      textureInfo[i].offset_mip1 = br.ReadUInt32();
      textureInfo[i].offset_mip2 = br.ReadUInt32();
      textureInfo[i].offset_mip3 = br.ReadUInt32();
    }

    Dictionary<int, int> bins = new Dictionary<int, int>();
    Dictionary<int, int> texIndexToArrayIndex = new Dictionary<int, int>();
    Material[] ArrayMaterials = CreateTextureArrays(ref textureInfo, ref br, ref texIndexToArrayIndex, ref bins, numTextures, textureDirectoryOffset, offset_lightmaps, length_lightmaps, path, rawPath);

    int numVertices = length_vertex / 12; // 3 4 byte ints
    Vector3[] vertices = new Vector3[numVertices];
    br.BaseStream.Seek(offset_vertex, SeekOrigin.Begin);
    for (int i = 0; i < numVertices; i++)
    {
      vertices[i].x = br.ReadSingle();
      vertices[i].y = br.ReadSingle();
      vertices[i].z = br.ReadSingle();
    }

    int numEdges = length_edge / 4; // 2 2 byte ints
    EdgeInfo[] edges = new EdgeInfo[numEdges];
    br.BaseStream.Seek(offset_edge, SeekOrigin.Begin);
    for (int i = 0; i < numEdges; i++)
    {
      byte[] rawData = br.ReadBytes(4);
      edges[i] = RawDataToStruct<EdgeInfo>(rawData);
    }

    int numEdgeList = length_ledge / 4;
    int[] edgeList = new int[numEdgeList];
    br.BaseStream.Seek(offset_ledge, SeekOrigin.Begin);
    for (int i = 0; i < numEdgeList; i++)
    {
      edgeList[i] = br.ReadInt32();
    }

    int numPlanes = length_planes / 20; // 4 4 byte floats + 1 4 byte int
    PlaneInfo[] planes = new PlaneInfo[numPlanes];
    br.BaseStream.Seek(offset_planes, SeekOrigin.Begin);
    for (int i = 0; i < numPlanes; i++)
    {
      byte[] rawData = br.ReadBytes(20);
      planes[i] = RawDataToStruct<PlaneInfo>(rawData);
    }

    int numFaces = length_faces / 20; //4 2 byte ints, 2 4 byte ints, and 4 1 byte chars
    FaceInfo[] faces = new FaceInfo[numFaces];
    br.BaseStream.Seek(offset_faces, SeekOrigin.Begin);
    for (int i = 0; i < numFaces; i++)
    {
      byte[] rawData = br.ReadBytes(20);
      faces[i] = RawDataToStruct<FaceInfo>(rawData);
    }
    Debug.Log(String.Format("Number of Faces, {0}", numFaces));

    int numMUVs = length_texinfo / 40; //8 4 byte floats, 2 4 byte ints
    MaterialUV[] materialUVs = new MaterialUV[numMUVs];
    br.BaseStream.Seek(offset_texinfo, SeekOrigin.Begin);
    for (int i = 0; i < numMUVs; i++)
    {
      byte[] rawData = br.ReadBytes(40);
      materialUVs[i] = RawDataToStruct<MaterialUV>(rawData);
    }

    /* Not Necessary, only used by the BSP lumps
    int numFaceList = length_lface / 4;
    int[] faceList = new int[numFaceList];
    br.BaseStream.Seek(offset_lface, SeekOrigin.Begin);
    for (int i = 0; i < numEdgeList; i++)
    {
      faceList[i] = br.ReadInt32();
    }
    */

    int numModels = length_model / 64; // model struct contains 9 floats and 7 ints, for a total of 64 bytes 
    modelInfo[] models = new modelInfo[numModels];
    br.BaseStream.Seek(offset_model, SeekOrigin.Begin);
    for (int i = 0; i < numModels; i++)
    {
      byte[] rawData = br.ReadBytes(64);
      models[i] = RawDataToStruct<modelInfo>(rawData);
    }
    Debug.Log(String.Format("Num Models {0}", numModels));
    GameObject MapPrefab = new GameObject();
    MapPrefab.name = String.Format("_{0}",Path.GetFileNameWithoutExtension(bspPath));
    GameObject[] Model_Root = new GameObject[numModels];
    GameObject[] ModelGO = new GameObject[numModels];
    GameObject TriggerRoot = new GameObject();


    for (int i = 0; i < numModels; i++)
    {
      //map of the indicies of each vertex used by the mesh from the global array to its index in the local array for the mesh
      int[] globalVertexListToLocal = new int[numVertices];
      //List<int> localVertexListToGlobal = new List<int>();
      //bool[] isVertexUsedByMesh = new bool[numVertices];
      List<Vector3> localMeshVerticies = new List<Vector3>();
      localMeshVerticies.Capacity = numVertices;
      List<Color32> localMeshVertexColors = new List<Color32>();
      localMeshVertexColors.Capacity = numVertices;
      List<Vector3> localMeshNormals = new List<Vector3>();
      localMeshNormals.Capacity = numVertices;
      List<Vector2> localMeshUV0 = new List<Vector2>();
      localMeshUV0.Capacity = numVertices;
      List<Vector2> localMeshUV1 = new List<Vector2>();
      localMeshUV1.Capacity = numVertices;
      List<Vector4> localMeshUV2 = new List<Vector4>();
      //localMeshUV2.Capacity = numVertices;

      int currentListIndex = 0;

      List<Material> meshMaterials = new List<Material>();
      // Find how many materials we are using on this mesh so we can create the appropriate number of vertex lists
      int modelNumMaterials = 0;
      Dictionary<int, int> materialHashToSubmeshIndex = new Dictionary<int, int>();
      for (int f = 0; f < models[i].face_num; f++)
      {
        int materialID = materialUVs[faces[models[i].face_id + f].texInfo_id].texture_id;
        int materialHash = textureInfo[materialID].dimHash;
        if (!materialHashToSubmeshIndex.ContainsKey(materialHash))
        {
          materialHashToSubmeshIndex.Add(materialHash, modelNumMaterials);
          meshMaterials.Add(ArrayMaterials[bins[materialHash]]);
          modelNumMaterials++;
        }
      }

      

      List<int>[] triangles = new List<int>[modelNumMaterials];
      for (int m = 0; m < modelNumMaterials; m++)
      {
        triangles[m] = new List<int>();
      }
      //triangles.Capacity = numVertices;


      for (int f = 0; f < models[i].face_num; f++)
      {
        FaceInfo face1 = faces[models[i].face_id + f];
        int beginEdgeList = face1.ledge_id;
        int edgeListLength = face1.ledge_num; // This is also the number of sides of our n-gon
        int[] ngonVtxList = new int[edgeListLength];

        // Find the normal stored in the BSP so we don't have to calculate it ourselves
        // note that in unity the Y and Z axes are reversed, so we swizzle them here
        PlaneInfo currentPlane = planes[face1.planeId];
        Vector3 normal = new Vector3(currentPlane.normalX, currentPlane.normalZ, currentPlane.normalY);
        normal = faces[models[i].face_id + f].side == 0 ? normal : -normal;
        normal = Vector3.Normalize(normal);

        MaterialUV faceMaterialUV = materialUVs[faces[models[i].face_id + f].texInfo_id];
        int materialID = faceMaterialUV.texture_id;
        int materialHash = textureInfo[materialID].dimHash;
        int subMesh = materialHashToSubmeshIndex[materialHash];

        // keep track of the minimum and maximum uv coordinates for the face for the lightmap
        float minU = float.MaxValue, minV = float.MaxValue;
        float maxU = float.MinValue, maxV = float.MinValue;
        //Make sure our face is actually not just a line or a vertex
        if (edgeListLength < 3)
        {
          Debug.LogWarning("Degenerate Face: Less than 3 Edges! Skipping");
        }
        else
        {
          // Process the edges listed by the edge list, referenced by the face
          for (int le = 0; le < edgeListLength; le++)
          {
            int edgeIndex = edgeList[beginEdgeList + le];

            // Quake stores the edge index as negative if the edge is going counterclockwise when used by this face
            bool clockwise = true;
            if (edgeIndex < 0)
            {
              edgeIndex = -edgeIndex;
              clockwise = false;
            }

            int vtx0Index;
            int vtx1Index;

            // reverse the order of the edge if it is counter-clockwise
            if (clockwise)
            {
              vtx0Index = edges[edgeIndex].vertex0;
              vtx1Index = edges[edgeIndex].vertex1;
            }
            else
            {
              vtx0Index = edges[edgeIndex].vertex1;
              vtx1Index = edges[edgeIndex].vertex0;
            }
            /*
            minX = Mathf.Min(vertices[vtx0Index].x, minX);
            minY = Mathf.Min(vertices[vtx0Index].y, minY);
            minZ = Mathf.Min(vertices[vtx0Index].z, minZ);
            maxX = Mathf.Max(vertices[vtx0Index].x, minX);
            maxY = Mathf.Max(vertices[vtx0Index].y, minY);
            maxZ = Mathf.Max(vertices[vtx0Index].z, minZ);
            */
            // Swizzle the vertex's position to make Y axis up instead of Z
            Vector3 vtx0 = new Vector3(vertices[vtx0Index].x, vertices[vtx0Index].z, vertices[vtx0Index].y);
            // scale down the world from Quake units. The player in Quake is 48 units high, and VRC's player collider height is 1.65, so scale by 1.67/48 to make sure the player can enter all areas
            vtx0 *= 1.67f / 58.0f;
            localMeshVerticies.Add(vtx0);
            localMeshNormals.Add(normal);

            //Add the light types as vertex colors
            Color32 vtxColor = new Color32((byte)face1.light0, (byte)face1.light1, (byte)face1.light2, (byte)face1.light3);
            localMeshVertexColors.Add(vtxColor);
            //localVertexListToGlobal.Add(vtx0Index);
            globalVertexListToLocal[vtx0Index] = currentListIndex;
            currentListIndex++;

            //Calculate the UV's from the S and T vectors stored in the materialInfo
            Vector2 vtxUV0 = UV_from_ST(vertices[vtx0Index], faceMaterialUV, textureInfo[materialID]);
            localMeshUV0.Add(vtxUV0);

            Vector2 vtxUV1 = LM_UV_from_ST(vertices[vtx0Index], faceMaterialUV);
            localMeshUV1.Add(vtxUV1);

            //find the min U and V of the lightmap so we can calculate its bounding box later on.
            minU = Mathf.Min(vtxUV1.x, minU);
            minV = Mathf.Min(vtxUV1.y, minV);
            maxU = Mathf.Max(vtxUV1.x, maxU);
            maxV = Mathf.Max(vtxUV1.y, maxV);

            // add the first vertex to the n-gon. We don't need to add the second vertex as presumably all edges are connected
            // in counter-clockwise fashion and thus it will also be listed by the next edge as the first vertex
            ngonVtxList[le] = vtx0Index;
          }

          //Put the binary data of the lightmap and texture array id into floats so we can store them as UV channel. The shader reading the mesh will treat the floats as an ints, getting the original numbers back.
          byte[] LmBytes = BitConverter.GetBytes(face1.lightMap_id);
          float LmIdAsFloat = BitConverter.ToSingle(LmBytes, 0);
          byte[] TiBytes = BitConverter.GetBytes(texIndexToArrayIndex[materialID]);
          float TiIdAsFloat = BitConverter.ToSingle(TiBytes, 0);

          //Calculate the UV bounding box of the polygon in the lightmap, rounded. Quake's lightmap texelsize is 1/16th of a worldspace unit, so divide by 16
          Vector2 minBB_UV = new Vector2(Mathf.Floor(minU / 16.0f), Mathf.Floor(minV / 16.0f));
          Vector2 maxBB_UV = new Vector2(Mathf.Ceil(maxU / 16.0f), Mathf.Ceil(maxV / 16.0f));
          Vector2 bounds = maxBB_UV - minBB_UV;

          Vector4 boundsLmTi = new Vector4(bounds.x, bounds.y, LmIdAsFloat, TiIdAsFloat);
          for (int UVIndex = localMeshUV1.Count - 1; UVIndex >= localMeshUV1.Count - edgeListLength; UVIndex--)
          {
            Vector2 centeredUV = (localMeshUV1[UVIndex] - minBB_UV);
            //Debug.Log(String.Format("LM UV: ({0}, {1})    min BB UV: ({2}, {3})    Centered UVs: ({4}, {5})", localMeshUV1[UVIndex].x, localMeshUV1[UVIndex].y, minBB_UV.x, minBB_UV.y, centeredUV.x, centeredUV.y));
            localMeshUV1[UVIndex] = ((localMeshUV1[UVIndex] / 16.0f) - minBB_UV) + new Vector2(0.5f, 0.5f);
            localMeshUV2.Add(boundsLmTi);
          }

          // Now that we have a list vertices which form a convex n-gon we need to make triangles out of the n-gon
          for (int v = 0; v < edgeListLength - 2; v += 1)
          {
            int index1 = ngonVtxList[0];
            int index2 = ngonVtxList[v + 1];
            int index3 = ngonVtxList[v + 2];
            triangles[subMesh].Add(globalVertexListToLocal[index1]);
            triangles[subMesh].Add(globalVertexListToLocal[index2]);
            triangles[subMesh].Add(globalVertexListToLocal[index3]);
          }
        }
      }

      //Debug.Log(String.Format("Number of Verticies, {0}", localMeshVerticies.Count));
      //Debug.Log(String.Format("Number of UV1, {0}", localMeshUV1.Count));
      //Debug.Log(String.Format("Number of UV2, {0}", localMeshUV2.Count));
      string exportMeshPath = path + "/" + String.Format("_{0}_{1}_mesh.asset", Path.GetFileNameWithoutExtension(bspPath), i);
      string fullExportMeshPath = rawPath + "/" + String.Format("_{0}_{1}_mesh.asset", Path.GetFileNameWithoutExtension(bspPath), i);
      Mesh exportMesh = new Mesh();
      if (File.Exists(fullExportMeshPath))
      {
        exportMesh = (Mesh)AssetDatabase.LoadAssetAtPath(exportMeshPath, typeof(Mesh));
      }
      exportMesh.subMeshCount = modelNumMaterials;
      exportMesh.SetVertices(localMeshVerticies);
      exportMesh.SetNormals(localMeshNormals);
      exportMesh.SetColors(localMeshVertexColors);
      exportMesh.SetUVs(0, localMeshUV0);
      exportMesh.SetUVs(1, localMeshUV1);
      exportMesh.SetUVs(2, localMeshUV2);
      for (int sm = 0; sm < modelNumMaterials; sm++)
        exportMesh.SetTriangles(triangles[sm], sm);

      if (!File.Exists(fullExportMeshPath))
      {
        AssetDatabase.CreateAsset(exportMesh, exportMeshPath);
      }
      AssetDatabase.SaveAssets();


      Model_Root[i] = new GameObject();
      Model_Root[i].transform.position = new Vector3(models[i].originX, models[i].originZ, models[i].originY);
      Model_Root[i].transform.SetParent(MapPrefab.transform);
      Model_Root[i].name = String.Format("{0}_{1}", Path.GetFileNameWithoutExtension(bspPath), i);
      ModelGO[i] = new GameObject();
      ModelGO[i].transform.position = new Vector3(models[i].originX, models[i].originZ, models[i].originY);
      ModelGO[i].transform.SetParent(Model_Root[i].transform);
      ModelGO[i].name = String.Format("{0}_{1}_mesh", Path.GetFileNameWithoutExtension(bspPath), i);
      MeshFilter meshFilter = ModelGO[i].AddComponent(typeof(MeshFilter)) as MeshFilter;
      meshFilter.sharedMesh = exportMesh;
      MeshRenderer meshRender = ModelGO[i].AddComponent(typeof(MeshRenderer)) as MeshRenderer;
      meshRender.materials = meshMaterials.ToArray();
    }
    PrefabUtility.SaveAsPrefabAsset(MapPrefab, path + "/" + String.Format("_{0}_prefab.prefab", Path.GetFileNameWithoutExtension(bspPath)));
    UnityEngine.Object.DestroyImmediate(MapPrefab);
    //Debug.Log(textureDirectoryOffset);
    //Debug.Log(textureDirectorySize);
    //Debug.Log(textureDirectoryOffset + textureOffsets[0]);

    br.Close();
    s.Close();
  }



  public Material[] CreateTextureArrays(ref TextureInfo[] textureInfo, ref BinaryReader br, ref Dictionary<int, int> texIndexToArrayIndex,
    ref Dictionary<int, int> bins, int numTextures, int textureDirectoryOffset, int offset_lightmaps, int length_lightmaps, string path, string rawPath)
  {
    // Sort the textures into bins based on their dimensions
    List<List<TextureInfo>> binnedTex = new List<List<TextureInfo>>();
   

    int binCounter = 0;
    for (int i = 0; i < numTextures; i++)
    {
      textureInfo[i].originalIndex = i;
      if (bins.ContainsKey(textureInfo[i].dimHash))
      {
        //textureInfo[i].materialIndex = bins[textureInfo[i].dimHash];
        binnedTex[bins[textureInfo[i].dimHash]].Add(textureInfo[i]);
      }
      else
      {
        //textureInfo[i].materialIndex = binCounter;
        bins.Add(textureInfo[i].dimHash, binCounter);
        binnedTex.Add(new List<TextureInfo>());
        binnedTex[binCounter].Add(textureInfo[i]);
        binCounter += 1;
        Debug.Log(String.Format("{0}, {1}", textureInfo[i].width, textureInfo[i].height));
      }
    }

    //Sort the textures within the bins by name
    foreach (List<TextureInfo> texList in binnedTex)
    {
      texList.Sort((t1, t2) => String.Compare(Regex.Replace(t1.Name, @"^(\+[\d\w])\s*(.+)", "$2$1"), Regex.Replace(t2.Name, @"^(\+[\d\w])\s*(.+)", "$2$1")));
    }

    // read each texture into the appropriate texture array
    Texture2DArray[] textureArrays = new Texture2DArray[bins.Count];
    Texture2D lightMap;
    string[] textureNames = new string[bins.Count];
    for (int i = 0; i < binnedTex.Count; i++)
    {
      if (binnedTex[i][0].width < 4096 && binnedTex[i][0].width > 0) //make sure we don't have a corrupt texture, to deal with torneko's map
      {
        textureArrays[i] = new Texture2DArray(binnedTex[i][0].width, binnedTex[i][0].height, binnedTex[i].Count, TextureFormat.R8, 4, true);
        textureArrays[i].filterMode = FilterMode.Point;
        for (int j = 0; j < binnedTex[i].Count; j++)
        {
          br.BaseStream.Seek(textureDirectoryOffset + binnedTex[i][j].offset_header + binnedTex[i][j].offset_mip0, SeekOrigin.Begin);
          byte[] data_mip0 = br.ReadBytes(binnedTex[i][j].width * binnedTex[i][j].height);
          data_mip0 = flipImage(data_mip0, binnedTex[i][j].width, binnedTex[i][j].height);
          textureArrays[i].SetPixelData(data_mip0, 0, j);

          br.BaseStream.Seek(textureDirectoryOffset + binnedTex[i][j].offset_header + binnedTex[i][j].offset_mip1, SeekOrigin.Begin);
          byte[] data_mip1 = br.ReadBytes((binnedTex[i][j].width * binnedTex[i][j].height) / 4);
          data_mip1 = flipImage(data_mip1, binnedTex[i][j].width / 2, binnedTex[i][j].height / 2);
          textureArrays[i].SetPixelData(data_mip1, 1, j);

          br.BaseStream.Seek(textureDirectoryOffset + binnedTex[i][j].offset_header + binnedTex[i][j].offset_mip2, SeekOrigin.Begin);
          byte[] data_mip2 = br.ReadBytes((binnedTex[i][j].width * binnedTex[i][j].height) / 16);
          data_mip2 = flipImage(data_mip2, binnedTex[i][j].width / 4, binnedTex[i][j].height / 4);
          textureArrays[i].SetPixelData(data_mip2, 2, j);

          br.BaseStream.Seek(textureDirectoryOffset + binnedTex[i][j].offset_header + binnedTex[i][j].offset_mip3, SeekOrigin.Begin);
          byte[] data_mip3 = br.ReadBytes((binnedTex[i][j].width * binnedTex[i][j].height) / 64);
          data_mip3 = flipImage(data_mip3, binnedTex[i][j].width / 8, binnedTex[i][j].height / 8);
          textureArrays[i].SetPixelData(data_mip3, 3, j);

          textureNames[i] += binnedTex[i][j].Name + "\n";
          texIndexToArrayIndex.Add(binnedTex[i][j].originalIndex, j);
        }
      }
      else
      {
        Debug.LogWarning(String.Format("CORRUPT TEXTURE of size {0}x{1}, skipping", binnedTex[i][0].width, binnedTex[i][0].height));
        textureArrays[i] = new Texture2DArray(2, 2, binnedTex[i].Count, TextureFormat.R8, 1, true);
        for (int j = 0; j < binnedTex[i].Count; j++)
        {
          textureNames[i] += binnedTex[i][j].Name + " CORRUPT \n";
          texIndexToArrayIndex.Add(binnedTex[i][j].originalIndex, j);
        }
      }
    }

    int lightmapDimSq = (int)Mathf.Ceil(Mathf.Sqrt((float)length_lightmaps));   // find the dimensions of a 1:1 texture that can contain the lightmap data
    int pow2dim = RoundUpToPow2(lightmapDimSq);                                 // find the smallest power of 2 dimensions that can contain the texture                                                                          // to make sure we aren't wasting enormous amounts of space, use a 1:0.5 texture if the nearest 1:1 power of 2 texture that can contain the data is more than twice as big as the data
    int[] lightmapDim = (lightmapDimSq * lightmapDimSq) >= (pow2dim * pow2dim / 2) ? new int[2] { pow2dim, pow2dim } : new int[2] { pow2dim, pow2dim / 2 };
    Debug.Log(String.Format("Lightmap Dimensions: {0}", lightmapDim));
    lightMap = new Texture2D(lightmapDim[0], lightmapDim[1], TextureFormat.R8, false, true);
    
    if (length_lightmaps > 0) //Don't try to copy data into the lightmap texture if there's no data!
    {
      br.BaseStream.Seek(offset_lightmaps, SeekOrigin.Begin);
      byte[] data_lm = new byte[lightmapDim[0] * lightmapDim[1]];
      br.ReadBytes(length_lightmaps).CopyTo(data_lm, 0);
      lightMap.SetPixelData(data_lm, 0);
    }

    if (path.Length != 0)
    {
      Texture2D LMLoad = new Texture2D(2,2);
      if (length_lightmaps > 0) //Don't save a lightmap texture if there's no lightmap
      {
        lightMap.Apply(false);
        string LMNameAndPath = path + "/" + String.Format("{0}_lightmap.asset", Path.GetFileNameWithoutExtension(bspPath));
        AssetDatabase.CreateAsset(lightMap, LMNameAndPath);
        LMLoad = (Texture2D)AssetDatabase.LoadAssetAtPath(LMNameAndPath, typeof(Texture2D));
      }
      Material[] ArrayMaterials = new Material[textureArrays.Length];
      for (int i = 0; i < textureArrays.Length; i++)
      {
        textureArrays[i].Apply(false);
        string textureNameAndPath = path + "/" + String.Format("{0}_{1}x{2}_tex2darray.asset", Path.GetFileNameWithoutExtension(bspPath), binnedTex[i][0].width, binnedTex[i][0].height);
        string fullTextureNameAndPath = rawPath + "/" + String.Format("{0}_{1}x{2}_tex2darray.asset", Path.GetFileNameWithoutExtension(bspPath), binnedTex[i][0].width, binnedTex[i][0].height);

        if (!File.Exists(fullTextureNameAndPath))
        {
          Texture2DArray tempTex = new Texture2DArray(2, 2, 1, TextureFormat.R8, false);
          AssetDatabase.CreateAsset(tempTex, textureNameAndPath);
        }
        Texture2DArray texLoad = (Texture2DArray)AssetDatabase.LoadAssetAtPath(textureNameAndPath, typeof(Texture2DArray));
        EditorUtility.CopySerialized(textureArrays[i], texLoad);

        string fileListName = String.Format("{0}_{1}x{2}_texNames.txt", Path.GetFileNameWithoutExtension(bspPath), binnedTex[i][0].width, binnedTex[i][0].height);
        System.IO.File.WriteAllText(rawPath + "/" + fileListName, textureNames[i]);
        AssetDatabase.ImportAsset(path + "/" + fileListName);

        Material newMat = new Material(texArrayShader);
        newMat.SetTexture("_PaletteTex", colorPalette);
        newMat.SetTexture("_StyleTex", lightStyles);
        if (length_lightmaps > 0) //Don't assign a lightmap texture if there's no lightmap
        {
          newMat.SetTexture("_LMTex", LMLoad);
        }
        else
        {
          newMat.SetInt("_FullBright", 1);
        }
        newMat.SetTexture("_MainTexArray", texLoad);
        string materialNameAndPath = path + "/" + String.Format("{0}_{1}x{2}.mat", Path.GetFileNameWithoutExtension(bspPath), binnedTex[i][0].width, binnedTex[i][0].height);
        string fullMaterialNameAndPath = path + "/" + String.Format("{0}_{1}x{2}.mat", Path.GetFileNameWithoutExtension(bspPath), binnedTex[i][0].width, binnedTex[i][0].height);

        if (!File.Exists(fullMaterialNameAndPath))
        {
          Material tempMat = new Material(texArrayShader);
          AssetDatabase.CreateAsset(tempMat, materialNameAndPath);
        }

        ArrayMaterials[i] = (Material)AssetDatabase.LoadAssetAtPath(materialNameAndPath, typeof(Material));
        EditorUtility.CopySerialized(newMat, ArrayMaterials[i]);
        AssetDatabase.SaveAssets();
      }
      return ArrayMaterials;
    }
    return null;
  }

  private byte[] flipImage(byte[] data, int width, int height)
  {
    byte[] flippedData = new byte[data.Length];
    for (int y = 0; y < height; y++)
    {
      int flippedY = height - y - 1;
      for (int x = 0; x < width; x++)
      {
        flippedData[x + width * flippedY] = data[x + width * y];
      }
    }
    return flippedData;
  }



}
