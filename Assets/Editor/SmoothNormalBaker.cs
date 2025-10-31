using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// 平滑法线烘焙工具 - 用于生成卡通描边所需的平滑法线
/// 将平滑法线存储到顶点颜色通道，供 Outline Shader 使用
/// </summary>
public class SmoothNormalBaker : EditorWindow
{
    private GameObject targetModel;
    private bool saveSeparateMesh = true;

    [MenuItem("Tools/Smooth Normal Baker")]
    public static void ShowWindow()
    {
        GetWindow<SmoothNormalBaker>("平滑法线烘焙工具");
    }

    private void OnGUI()
    {
        GUILayout.Label("卡通描边平滑法线烘焙工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "此工具将平滑法线烘焙到模型的顶点颜色通道中，用于解决描边断续问题。\n\n" +
            "使用步骤：\n" +
            "1. 拖入需要处理的模型（带有 MeshFilter 或 SkinnedMeshRenderer 的 GameObject）\n" +
            "2. 点击 '烘焙平滑法线' 按钮\n" +
            "3. 在 Shader 中使用 color.rgb 作为平滑法线\n\n" +
            "优势：不影响 Tangent，保留法线贴图的正确性",
            MessageType.Info);

        EditorGUILayout.Space();

        targetModel = (GameObject)EditorGUILayout.ObjectField("目标模型", targetModel, typeof(GameObject), true);
        saveSeparateMesh = EditorGUILayout.Toggle("保存为新 Mesh 资源", saveSeparateMesh);

        EditorGUILayout.Space();

        GUI.enabled = targetModel != null;
        if (GUILayout.Button("烘焙平滑法线到顶点颜色", GUILayout.Height(40)))
        {
            BakeSmoothNormals();
        }
        GUI.enabled = true;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "技术说明：\n" +
            "• 平滑法线存储位置：顶点颜色 (Color.rgb)\n" +
            "• 编码方式：法线 [-1,1] → 顶点色 [0,1]，Alpha = 1.0\n" +
            "• 不影响 Tangent，保留法线贴图的正确性\n" +
            "• 生成的 Mesh 会保存在 Assets/Baked/ 文件夹中\n" +
            "• 米哈游（原神/崩坏3/绝区零）同款方案",
            MessageType.None);
    }

    private void BakeSmoothNormals()
    {
        MeshFilter meshFilter = targetModel.GetComponent<MeshFilter>();
        SkinnedMeshRenderer skinnedMeshRenderer = targetModel.GetComponent<SkinnedMeshRenderer>();

        Mesh srcMesh = null;
        if (meshFilter != null)
            srcMesh = meshFilter.sharedMesh;
        else if (skinnedMeshRenderer != null)
            srcMesh = skinnedMeshRenderer.sharedMesh;
        else
        {
            EditorUtility.DisplayDialog("错误", "选中的对象没有 MeshFilter 或 SkinnedMeshRenderer 组件！", "确定");
            return;
        }

        if (srcMesh == null)
        {
            EditorUtility.DisplayDialog("错误", "无法获取 Mesh 数据！", "确定");
            return;
        }

        // 生成平滑法线
        Vector3[] smoothNormals = GenerateSmoothNormals(srcMesh);

        // 创建新的 Mesh 副本
        Mesh bakedMesh = saveSeparateMesh ? Instantiate(srcMesh) : srcMesh;
        bakedMesh.name = srcMesh.name + "_SmoothNormal";

        // 将平滑法线存储到顶点颜色（RGB编码法线，范围 [0,1]）
        Color[] colors = new Color[smoothNormals.Length];
        for (int i = 0; i < smoothNormals.Length; i++)
        {
            // 将法线从 [-1,1] 映射到 [0,1]
            colors[i] = new Color(
                smoothNormals[i].x * 0.5f + 0.5f,
                smoothNormals[i].y * 0.5f + 0.5f,
                smoothNormals[i].z * 0.5f + 0.5f,
                1.0f // Alpha 通道设为1
            );
        }
        bakedMesh.colors = colors;

        // 保存 Mesh 资源
        if (saveSeparateMesh)
        {
            string folderPath = "Assets/Baked";
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder("Assets", "Baked");

            string path = $"{folderPath}/{bakedMesh.name}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            AssetDatabase.CreateAsset(bakedMesh, path);
            AssetDatabase.SaveAssets();

            // 应用到当前模型
            if (meshFilter != null)
                meshFilter.sharedMesh = bakedMesh;
            else if (skinnedMeshRenderer != null)
                skinnedMeshRenderer.sharedMesh = bakedMesh;

            EditorUtility.DisplayDialog("成功",
                $"平滑法线烘焙完成！\n\n" +
                $"保存路径：{path}\n" +
                $"顶点数：{smoothNormals.Length}\n" +
                $"存储方式：顶点颜色 (Color.rgb)\n\n" +
                $"请在 Shader 中使用解码后的顶点颜色作为平滑法线：\n" +
                $"float3 smoothNormal = color.rgb * 2.0 - 1.0;",
                "确定");

            // 高亮显示生成的资源
            EditorGUIUtility.PingObject(bakedMesh);
        }
        else
        {
            EditorUtility.SetDirty(srcMesh);
            AssetDatabase.SaveAssets();
            
            EditorUtility.DisplayDialog("成功",
                $"平滑法线已直接写入原始 Mesh！\n\n" +
                $"顶点数：{smoothNormals.Length}\n" +
                $"存储方式：顶点颜色 (Color.rgb)\n\n" +
                $"请在 Shader 中使用解码后的顶点颜色作为平滑法线：\n" +
                $"float3 smoothNormal = color.rgb * 2.0 - 1.0;",
                "确定");
        }
    }

    /// <summary>
    /// 生成平滑法线（核心算法）
    /// 原理：将位置相同的顶点的法线进行平均
    /// </summary>
    public static Vector3[] GenerateSmoothNormals(Mesh _srcMesh)
    {
        Vector3[] vertices = _srcMesh.vertices;
        Vector3[] normals = _srcMesh.normals;
        Vector3[] smoothNormals = new Vector3[normals.Length];

        // 将法线复制一份
        System.Array.Copy(normals, smoothNormals, normals.Length);

        // 按顶点位置分组（位置相同的顶点分为一组）
        // 使用字典来提高性能
        Dictionary<Vector3, List<int>> vertexGroups = new Dictionary<Vector3, List<int>>();
        
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!vertexGroups.ContainsKey(vertices[i]))
                vertexGroups[vertices[i]] = new List<int>();
            vertexGroups[vertices[i]].Add(i);
        }

        // 对每一组相同位置的顶点计算平均法线
        foreach (var group in vertexGroups.Values)
        {
            // 如果只有一个顶点，不需要平滑
            if (group.Count == 1)
                continue;

            // 计算平均法线
            Vector3 smoothNormal = Vector3.zero;
            foreach (int index in group)
            {
                smoothNormal += normals[index];
            }
            smoothNormal.Normalize();

            // 将平滑后的法线赋值给所有同位置的顶点
            foreach (int index in group)
            {
                smoothNormals[index] = smoothNormal;
            }
        }

        return smoothNormals;
    }

    /// <summary>
    /// 批量处理场景中选中的所有模型
    /// </summary>
    [MenuItem("Tools/Batch Bake Selected Models")]
    public static void BatchBakeSelected()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        if (selectedObjects.Length == 0)
        {
            EditorUtility.DisplayDialog("提示", "请先选择要处理的模型！", "确定");
            return;
        }

        int count = 0;
        string folderPath = "Assets/Baked";
        if (!AssetDatabase.IsValidFolder(folderPath))
            AssetDatabase.CreateFolder("Assets", "Baked");

        foreach (GameObject obj in selectedObjects)
        {
            MeshFilter mf = obj.GetComponent<MeshFilter>();
            SkinnedMeshRenderer smr = obj.GetComponent<SkinnedMeshRenderer>();

            Mesh srcMesh = null;
            if (mf != null)
                srcMesh = mf.sharedMesh;
            else if (smr != null)
                srcMesh = smr.sharedMesh;

            if (srcMesh == null)
                continue;

            Vector3[] smoothNormals = GenerateSmoothNormals(srcMesh);
            Mesh bakedMesh = Instantiate(srcMesh);
            bakedMesh.name = srcMesh.name + "_SmoothNormal";

            // 将平滑法线存储到顶点颜色
            Color[] colors = new Color[smoothNormals.Length];
            for (int i = 0; i < smoothNormals.Length; i++)
            {
                colors[i] = new Color(
                    smoothNormals[i].x * 0.5f + 0.5f,
                    smoothNormals[i].y * 0.5f + 0.5f,
                    smoothNormals[i].z * 0.5f + 0.5f,
                    1.0f
                );
            }
            bakedMesh.colors = colors;

            string path = $"{folderPath}/{bakedMesh.name}.asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(bakedMesh, path);

            if (mf != null)
                mf.sharedMesh = bakedMesh;
            else if (smr != null)
                smr.sharedMesh = bakedMesh;

            count++;
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("批量烘焙完成", $"已成功处理 {count} 个模型！\n\n保存路径：{folderPath}", "确定");
    }
}
