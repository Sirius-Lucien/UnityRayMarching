using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class MeshSeparator : EditorWindow
{
    private GameObject targetObject;
    private bool preserveOriginal = true;
    private string newMeshName = "SeparatedMesh";
    
    [MenuItem("Tools/Mesh Separator")]
    public static void ShowWindow()
    {
        GetWindow<MeshSeparator>("Mesh Separator");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Mesh Vertex Separator", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);
        preserveOriginal = EditorGUILayout.Toggle("Preserve Original", preserveOriginal);
        newMeshName = EditorGUILayout.TextField("New Mesh Name", newMeshName);
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Separate Vertices"))
        {
            if (targetObject != null)
            {
                SeparateMeshVertices();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Please select a target object!", "OK");
            }
        }
        
        GUILayout.Space(10);
        EditorGUILayout.HelpBox("这个工具会将选中物体的Mesh进行拆边处理，" +
                               "让每个三角面都有独立的顶点，用于消散效果。", MessageType.Info);
    }
    
    private void SeparateMeshVertices()
    {
        MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Error", "Target object must have a MeshFilter with a valid mesh!", "OK");
            return;
        }
        
        Mesh originalMesh = meshFilter.sharedMesh;
        Mesh separatedMesh = CreateSeparatedMesh(originalMesh);
        
        // 保存为Asset
        string path = $"Assets/{newMeshName}.asset";
        AssetDatabase.CreateAsset(separatedMesh, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();
        
        // 应用到物体
        if (!preserveOriginal)
        {
            meshFilter.mesh = separatedMesh;
        }
        else
        {
            // 创建新物体
            GameObject newObj = Instantiate(targetObject);
            newObj.name = targetObject.name + "_Separated";
            newObj.GetComponent<MeshFilter>().mesh = separatedMesh;
        }
        
        Debug.Log($"拆边完成！原始三角面数: {originalMesh.triangles.Length/3}, " +
                 $"拆边后顶点数: {separatedMesh.vertexCount}");
        
        EditorUtility.DisplayDialog("Success", 
            $"Mesh separation completed!\n" +
            $"Original triangles: {originalMesh.triangles.Length/3}\n" +
            $"New vertices: {separatedMesh.vertexCount}\n" +
            $"Saved as: {path}", "OK");
    }
    
    private Mesh CreateSeparatedMesh(Mesh originalMesh)
    {
        // 获取原始数据
        Vector3[] oldVertices = originalMesh.vertices;
        Vector3[] oldNormals = originalMesh.normals;
        Vector2[] oldUVs = originalMesh.uv;
        Vector4[] oldTangents = originalMesh.tangents;
        int[] oldTriangles = originalMesh.triangles;
        
        // 创建新的数组，每个三角面都有独立的顶点
        int triangleCount = oldTriangles.Length / 3;
        Vector3[] newVertices = new Vector3[oldTriangles.Length];
        Vector3[] newNormals = new Vector3[oldTriangles.Length];
        Vector2[] newUVs = new Vector2[oldTriangles.Length];
        Vector4[] newTangents = new Vector4[oldTriangles.Length];
        int[] newTriangles = new int[oldTriangles.Length];
        
        // 为每个三角面创建独立的顶点
        for (int i = 0; i < triangleCount; i++)
        {
            int baseIndex = i * 3;
            
            for (int j = 0; j < 3; j++)
            {
                int oldIndex = oldTriangles[baseIndex + j];
                int newIndex = baseIndex + j;
                
                // 复制顶点数据
                newVertices[newIndex] = oldVertices[oldIndex];
                newTriangles[newIndex] = newIndex; // 新的索引就是连续的
                
                // 复制法线
                if (oldNormals != null && oldNormals.Length > oldIndex)
                    newNormals[newIndex] = oldNormals[oldIndex];
                
                // 复制UV
                if (oldUVs != null && oldUVs.Length > oldIndex)
                    newUVs[newIndex] = oldUVs[oldIndex];
                
                // 复制切线
                if (oldTangents != null && oldTangents.Length > oldIndex)
                    newTangents[newIndex] = oldTangents[oldIndex];
            }
        }
        
        // 创建新Mesh
        Mesh newMesh = new Mesh();
        newMesh.name = newMeshName;
        
        // 设置数据
        newMesh.vertices = newVertices;
        newMesh.triangles = newTriangles;
        
        if (newNormals[0] != Vector3.zero)
            newMesh.normals = newNormals;
        else
            newMesh.RecalculateNormals();
            
        if (newUVs[0] != Vector2.zero)
            newMesh.uv = newUVs;
            
        if (newTangents[0] != Vector4.zero)
            newMesh.tangents = newTangents;
        else
            newMesh.RecalculateTangents();
        
        newMesh.RecalculateBounds();
        
        return newMesh;
    }
}

// 右键菜单快捷方式
public class MeshSeparatorContextMenu
{
    [MenuItem("GameObject/3D Object/Separate Mesh Vertices", false, 0)]
    public static void SeparateSelectedMesh()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a GameObject first!", "OK");
            return;
        }
        
        MeshFilter meshFilter = selected.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Error", "Selected object must have a MeshFilter with a valid mesh!", "OK");
            return;
        }
        
        // 直接处理
        Mesh originalMesh = meshFilter.sharedMesh;
        Mesh separatedMesh = CreateSeparatedMeshStatic(originalMesh);
        
        // 保存为Asset
        string path = $"Assets/{selected.name}_Separated.asset";
        AssetDatabase.CreateAsset(separatedMesh, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();
        
        // 创建新物体
        GameObject newObj = Object.Instantiate(selected);
        newObj.name = selected.name + "_Separated";
        newObj.GetComponent<MeshFilter>().mesh = separatedMesh;
        
        Selection.activeGameObject = newObj;
        
        Debug.Log($"快速拆边完成！顶点数从 {originalMesh.vertexCount} 增加到 {separatedMesh.vertexCount}");
    }
    
    private static Mesh CreateSeparatedMeshStatic(Mesh originalMesh)
    {
        Vector3[] oldVertices = originalMesh.vertices;
        Vector3[] oldNormals = originalMesh.normals;
        Vector2[] oldUVs = originalMesh.uv;
        int[] oldTriangles = originalMesh.triangles;
        
        int triangleCount = oldTriangles.Length / 3;
        Vector3[] newVertices = new Vector3[oldTriangles.Length];
        Vector3[] newNormals = new Vector3[oldTriangles.Length];
        Vector2[] newUVs = new Vector2[oldTriangles.Length];
        int[] newTriangles = new int[oldTriangles.Length];
        
        for (int i = 0; i < triangleCount; i++)
        {
            int baseIndex = i * 3;
            for (int j = 0; j < 3; j++)
            {
                int oldIndex = oldTriangles[baseIndex + j];
                int newIndex = baseIndex + j;
                
                newVertices[newIndex] = oldVertices[oldIndex];
                newTriangles[newIndex] = newIndex;
                
                if (oldNormals.Length > oldIndex)
                    newNormals[newIndex] = oldNormals[oldIndex];
                if (oldUVs.Length > oldIndex)
                    newUVs[newIndex] = oldUVs[oldIndex];
            }
        }
        
        Mesh newMesh = new Mesh();
        newMesh.vertices = newVertices;
        newMesh.triangles = newTriangles;
        newMesh.normals = newNormals;
        newMesh.uv = newUVs;
        newMesh.RecalculateBounds();
        
        return newMesh;
    }
}