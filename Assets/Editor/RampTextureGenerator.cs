using UnityEngine;
using UnityEditor;
using System.IO;

public class RampTextureGenerator : EditorWindow
{
    // 四个颜色点
    private Color darkestColor = new Color(0.05f, 0.05f, 0.08f); // 最深色（极暗部/AO区域）
    private Color darkColor = new Color(0.2f, 0.18f, 0.22f);     // 深色（暗部）
    private Color midColor = new Color(0.6f, 0.55f, 0.6f);       // 中间色（基础色）
    private Color lightColor = new Color(0.95f, 0.92f, 0.88f);   // 浅色（亮部/高光）
    
    // 渐变控制点位置（0~1）
    private float point1 = 0.3f;   // 最深色 → 深色的分界点
    private float point2 = 0.65f;  // 深色 → 中间色的分界点
    private float point3 = 0.9f;   // 中间色 → 浅色的分界点
    
    // 贴图尺寸
    private int textureWidth = 256;
    private int textureHeight = 1;
    
    // 预览
    private Texture2D previewTexture;
    private bool autoUpdate = true;
    
    // 保存路径
    private string savePath = "Assets/Models/nike/Mat/";
    private string fileName = "RampTexture";
    
    // 输入Ramp图
    private Texture2D inputRampTexture;
    
    [MenuItem("Tools/Ramp Texture Generator")]
    public static void ShowWindow()
    {
        var window = GetWindow<RampTextureGenerator>("Ramp Generator");
        window.minSize = new Vector2(400, 600);
        window.Show();
    }
    
    private void OnEnable()
    {
        GeneratePreview();
    }
    
    private void OnGUI()
    {
        GUILayout.Label("Ramp Texture Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        // ========== 输入Ramp图 ==========
        EditorGUILayout.LabelField("从现有Ramp图提取参数", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        inputRampTexture = (Texture2D)EditorGUILayout.ObjectField("输入Ramp图", inputRampTexture, typeof(Texture2D), false);
        
        if (EditorGUI.EndChangeCheck() && inputRampTexture != null)
        {
            ExtractColorsFromRamp();
        }
        
        if (inputRampTexture != null && GUILayout.Button("从Ramp图提取颜色和参数", GUILayout.Height(30)))
        {
            ExtractColorsFromRamp();
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();
        
        // ========== 颜色设置 ==========
        EditorGUILayout.LabelField("颜色设置", EditorStyles.boldLabel);
        
        EditorGUI.BeginChangeCheck();
        
        darkestColor = EditorGUILayout.ColorField("最深色（极暗部）", darkestColor);
        darkColor = EditorGUILayout.ColorField("深色（阴影）", darkColor);
        midColor = EditorGUILayout.ColorField("中间色（基础）", midColor);
        lightColor = EditorGUILayout.ColorField("浅色（高光）", lightColor);
        
        EditorGUILayout.Space();
        
        // ========== 渐变控制 ==========
        EditorGUILayout.LabelField("渐变控制（三段渐变）", EditorStyles.boldLabel);
        
        point1 = EditorGUILayout.Slider("分界点1（极暗→暗）", point1, 0f, 1f);
        point2 = EditorGUILayout.Slider("分界点2（暗→中）", point2, 0f, 1f);
        point3 = EditorGUILayout.Slider("分界点3（中→亮）", point3, 0f, 1f);
        
        // 确保分界点顺序正确
        if (point2 < point1) point2 = point1;
        if (point3 < point2) point3 = point2;
        
        EditorGUILayout.Space();
        
        // ========== 贴图设置 ==========
        EditorGUILayout.LabelField("贴图设置", EditorStyles.boldLabel);
        
        textureWidth = EditorGUILayout.IntSlider("宽度", textureWidth, 64, 1024);
        textureHeight = EditorGUILayout.IntField("高度", textureHeight);
        textureHeight = Mathf.Max(1, textureHeight);
        
        EditorGUILayout.Space();
        
        // ========== 自动更新 ==========
        autoUpdate = EditorGUILayout.Toggle("实时预览", autoUpdate);
        
        if (EditorGUI.EndChangeCheck() && autoUpdate)
        {
            GeneratePreview();
        }
        
        EditorGUILayout.Space();
        
        // ========== 手动更新按钮 ==========
        if (!autoUpdate && GUILayout.Button("更新预览", GUILayout.Height(30)))
        {
            GeneratePreview();
        }
        
        EditorGUILayout.Space();
        
        // ========== 预览 ==========
        EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);
        
        if (previewTexture != null)
        {
            Rect previewRect = GUILayoutUtility.GetRect(textureWidth, 100, GUILayout.ExpandWidth(true));
            // 绘制棋盘背景
            DrawCheckerboard(previewRect);
            // 绘制预览贴图
            GUI.DrawTexture(previewRect, previewTexture, ScaleMode.StretchToFill, true);
        }
        
        EditorGUILayout.Space();
        
        // ========== 保存设置 ==========
        EditorGUILayout.LabelField("保存设置", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        savePath = EditorGUILayout.TextField("保存路径", savePath);
        if (GUILayout.Button("浏览", GUILayout.Width(60)))
        {
            string folder = EditorUtility.OpenFolderPanel("选择保存路径", "Assets", "");
            if (!string.IsNullOrEmpty(folder))
            {
                // 转换为相对路径
                if (folder.StartsWith(Application.dataPath))
                {
                    savePath = "Assets" + folder.Substring(Application.dataPath.Length);
                }
            }
        }
        EditorGUILayout.EndHorizontal();
        
        fileName = EditorGUILayout.TextField("文件名", fileName);
        
        EditorGUILayout.Space();
        
        // ========== 保存按钮 ==========
        if (GUILayout.Button("保存为 PNG", GUILayout.Height(40)))
        {
            SaveTexture();
        }
        
        EditorGUILayout.Space();
        
        // ========== 预设按钮 ==========
        EditorGUILayout.LabelField("预设", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("原神风格"))
        {
            darkestColor = new Color(0.08f, 0.08f, 0.12f);
            darkColor = new Color(0.2f, 0.18f, 0.22f);
            midColor = new Color(0.65f, 0.6f, 0.65f);
            lightColor = new Color(0.95f, 0.92f, 0.88f);
            point1 = 0.25f;
            point2 = 0.7f;
            point3 = 0.92f;
            if (autoUpdate) GeneratePreview();
        }
        
        if (GUILayout.Button("柔和皮肤"))
        {
            darkestColor = new Color(0.15f, 0.1f, 0.1f);
            darkColor = new Color(0.35f, 0.28f, 0.25f);
            midColor = new Color(0.75f, 0.65f, 0.6f);
            lightColor = new Color(0.98f, 0.95f, 0.92f);
            point1 = 0.2f;
            point2 = 0.6f;
            point3 = 0.88f;
            if (autoUpdate) GeneratePreview();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("金属质感"))
        {
            darkestColor = new Color(0.02f, 0.02f, 0.05f);
            darkColor = new Color(0.15f, 0.15f, 0.2f);
            midColor = new Color(0.5f, 0.5f, 0.55f);
            lightColor = new Color(0.95f, 0.95f, 1.0f);
            point1 = 0.3f;
            point2 = 0.55f;
            point3 = 0.82f;
            if (autoUpdate) GeneratePreview();
        }
        
        if (GUILayout.Button("布料标准"))
        {
            darkestColor = new Color(0.08f, 0.08f, 0.1f);
            darkColor = new Color(0.25f, 0.23f, 0.27f);
            midColor = new Color(0.6f, 0.55f, 0.6f);
            lightColor = new Color(0.9f, 0.88f, 0.85f);
            point1 = 0.25f;
            point2 = 0.68f;
            point3 = 0.9f;
            if (autoUpdate) GeneratePreview();
        }
        EditorGUILayout.EndHorizontal();
    }
    
    private void GeneratePreview()
    {
        if (previewTexture == null || previewTexture.width != textureWidth || previewTexture.height != textureHeight)
        {
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
            }
            previewTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            previewTexture.wrapMode = TextureWrapMode.Clamp;
            previewTexture.filterMode = FilterMode.Bilinear;
        }
        
        for (int y = 0; y < textureHeight; y++)
        {
            for (int x = 0; x < textureWidth; x++)
            {
                float t = (float)x / (textureWidth - 1);
                Color color = SampleGradient(t);
                previewTexture.SetPixel(x, y, color);
            }
        }
        
        previewTexture.Apply();
    }
    
    private Color SampleGradient(float t)
    {
        // t: 0~1
        // 四色三段渐变：darkestColor → darkColor → midColor → lightColor
        
        if (t <= point1)
        {
            // 第一段：最深色 → 深色
            float localT = t / Mathf.Max(0.001f, point1);
            return Color.Lerp(darkestColor, darkColor, localT);
        }
        else if (t <= point2)
        {
            // 第二段：深色 → 中间色
            float localT = (t - point1) / Mathf.Max(0.001f, point2 - point1);
            return Color.Lerp(darkColor, midColor, localT);
        }
        else if (t <= point3)
        {
            // 第三段：中间色 → 浅色
            float localT = (t - point2) / Mathf.Max(0.001f, point3 - point2);
            return Color.Lerp(midColor, lightColor, localT);
        }
        else
        {
            // 超出部分：保持浅色
            return lightColor;
        }
    }
    
    private void ExtractColorsFromRamp()
    {
        if (inputRampTexture == null)
        {
            EditorUtility.DisplayDialog("错误", "请先选择一个Ramp贴图！", "确定");
            return;
        }
        
        // 确保贴图可读
        string path = AssetDatabase.GetAssetPath(inputRampTexture);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        
        bool needsReimport = false;
        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            needsReimport = true;
        }
        
        if (needsReimport)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        
        try
        {
            int width = inputRampTexture.width;
            int height = inputRampTexture.height;
            
            // 从中间行采样（如果是多行贴图）
            int sampleY = height / 2;
            
            // 采样四个关键位置的颜色
            // 使用0%, 25%, 75%, 100%作为采样点，然后分析找到分界点
            darkestColor = inputRampTexture.GetPixel(0, sampleY);
            lightColor = inputRampTexture.GetPixel(width - 1, sampleY);
            
            // 采样整个宽度来找到颜色变化最大的位置
            Color[] colors = new Color[width];
            for (int x = 0; x < width; x++)
            {
                colors[x] = inputRampTexture.GetPixel(x, sampleY);
            }
            
            // 计算颜色差异（梯度）
            float[] gradients = new float[width - 1];
            for (int i = 0; i < width - 1; i++)
            {
                gradients[i] = ColorDistance(colors[i], colors[i + 1]);
            }
            
            // 使用简化的方法：将贴图分成4段，取每段的代表色
            int segment = width / 4;
            darkestColor = colors[segment / 2];
            darkColor = colors[segment + segment / 2];
            midColor = colors[2 * segment + segment / 2];
            lightColor = colors[3 * segment + segment / 2];
            
            // 寻找三个最大梯度变化点作为分界点
            // 将梯度数组分成三个区域，在每个区域找最大值
            int region1End = width / 3;
            int region2End = 2 * width / 3;
            
            int maxIdx1 = FindMaxGradientIndex(gradients, 0, region1End);
            int maxIdx2 = FindMaxGradientIndex(gradients, region1End, region2End);
            int maxIdx3 = FindMaxGradientIndex(gradients, region2End, width - 1);
            
            point1 = (float)maxIdx1 / (width - 1);
            point2 = (float)maxIdx2 / (width - 1);
            point3 = (float)maxIdx3 / (width - 1);
            
            // 确保点的顺序正确
            if (point1 > point2)
            {
                float temp = point1;
                point1 = point2;
                point2 = temp;
            }
            if (point2 > point3)
            {
                float temp = point2;
                point2 = point3;
                point3 = temp;
            }
            if (point1 > point2)
            {
                float temp = point1;
                point1 = point2;
                point2 = temp;
            }
            
            // 更新宽度匹配输入贴图
            textureWidth = width;
            textureHeight = height;
            
            // 生成预览
            GeneratePreview();
            
            EditorUtility.DisplayDialog("成功", 
                $"已从Ramp图提取参数：\n" +
                $"颜色1: RGB({darkestColor.r:F2}, {darkestColor.g:F2}, {darkestColor.b:F2})\n" +
                $"颜色2: RGB({darkColor.r:F2}, {darkColor.g:F2}, {darkColor.b:F2})\n" +
                $"颜色3: RGB({midColor.r:F2}, {midColor.g:F2}, {midColor.b:F2})\n" +
                $"颜色4: RGB({lightColor.r:F2}, {lightColor.g:F2}, {lightColor.b:F2})\n\n" +
                $"分界点1: {point1:F3}\n" +
                $"分界点2: {point2:F3}\n" +
                $"分界点3: {point3:F3}", 
                "确定");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("错误", $"提取颜色失败：\n{e.Message}\n\n请确保贴图可读(Read/Write Enabled)", "确定");
        }
    }
    
    private float ColorDistance(Color a, Color b)
    {
        // 计算两个颜色之间的欧几里得距离
        float dr = a.r - b.r;
        float dg = a.g - b.g;
        float db = a.b - b.b;
        return Mathf.Sqrt(dr * dr + dg * dg + db * db);
    }
    
    private int FindMaxGradientIndex(float[] gradients, int start, int end)
    {
        int maxIdx = start;
        float maxVal = 0f;
        
        for (int i = start; i < end && i < gradients.Length; i++)
        {
            if (gradients[i] > maxVal)
            {
                maxVal = gradients[i];
                maxIdx = i;
            }
        }
        
        return maxIdx;
    }
    
    private void DrawCheckerboard(Rect rect)
    {
        // 绘制棋盘背景（用于显示透明度）
        int checkerSize = 10;
        Color color1 = new Color(0.7f, 0.7f, 0.7f);
        Color color2 = new Color(0.9f, 0.9f, 0.9f);
        
        for (int y = 0; y < rect.height; y += checkerSize)
        {
            for (int x = 0; x < rect.width; x += checkerSize)
            {
                bool isEven = ((x / checkerSize) + (y / checkerSize)) % 2 == 0;
                Color color = isEven ? color1 : color2;
                EditorGUI.DrawRect(new Rect(rect.x + x, rect.y + y, checkerSize, checkerSize), color);
            }
        }
    }
    
    private void SaveTexture()
    {
        if (previewTexture == null)
        {
            EditorUtility.DisplayDialog("错误", "请先生成预览！", "确定");
            return;
        }
        
        // 确保路径存在
        if (!Directory.Exists(savePath))
        {
            Directory.CreateDirectory(savePath);
        }
        
        // 生成完整路径
        string fullPath = Path.Combine(savePath, fileName + ".png");
        
        // 编码为 PNG
        byte[] pngData = previewTexture.EncodeToPNG();
        
        if (pngData != null)
        {
            File.WriteAllBytes(fullPath, pngData);
            AssetDatabase.Refresh();
            
            // 设置导入设置
            TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                
                AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);
            }
            
            EditorUtility.DisplayDialog("成功", $"Ramp 贴图已保存到：\n{fullPath}", "确定");
            
            // 选中保存的资源
            Object savedAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
            Selection.activeObject = savedAsset;
            EditorGUIUtility.PingObject(savedAsset);
        }
        else
        {
            EditorUtility.DisplayDialog("错误", "无法编码为 PNG！", "确定");
        }
    }
    
    private void OnDestroy()
    {
        if (previewTexture != null)
        {
            DestroyImmediate(previewTexture);
        }
    }
}
