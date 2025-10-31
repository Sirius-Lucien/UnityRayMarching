using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ZZZ.EditorTools
{
    public sealed class TextureChannelViewer : EditorWindow
    {
        private enum Channel
        {
            Red,
            Green,
            Blue,
            Alpha
        }

        private const string LinkedShaderAssetPath = "Assets/Models/nike/Mat/ViewLightMap.shader";
        private const string LinkedShaderName = "Unlit/ViewLightMap";

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ChannelMaskId = Shader.PropertyToID("_ChannelMask");
        private static readonly int UseSrgbSamplingId = Shader.PropertyToID("_UseSRGBSampling");
        private static readonly int ShowAlphaAsMaskId = Shader.PropertyToID("_ShowAlphaAsMask");
        private static readonly int LightMapTexId = Shader.PropertyToID("_LightMap");
        private static readonly Regex LightMapSampleRegex = new Regex(
            @"SAMPLE_TEXTURE2D\(_LightMap,\s*sampler_LightMap,\s*IN\.uv\)\.(?<channel>[rgba])",
            RegexOptions.Compiled);

        private Texture2D _texture;
        private Channel _channel = Channel.Red;
        private bool _useSrgbSampling = true;
        private bool _treatAlphaAsMask = false;
        private Material _previewMaterial;
        private bool _pendingMaterialSync;
        private Texture2D _samplePixelBuffer;
        private Vector2 _lastSampleUv;
        private float _lastSampleValue;
        private Color _lastSampleColor;
        private bool _hasSample;

        [MenuItem("Tools/Texture Channel Viewer", priority = 2000)]
        private static void Open()
        {
            var window = GetWindow<TextureChannelViewer>(false, "Channel Viewer", true);
            window.minSize = new Vector2(320f, 320f);
            window.Show();
        }

        [MenuItem("Assets/Preview Texture Channels", priority = 2100)]
        private static void OpenForSelection()
        {
            var window = GetWindow<TextureChannelViewer>(false, "Channel Viewer", true);
            window.minSize = new Vector2(320f, 320f);
            if (Selection.activeObject is Texture2D tex)
            {
                window._texture = tex;
                window._pendingMaterialSync = true;
            }
            window.Show();
        }

        [MenuItem("Assets/Preview Texture Channels", true)]
        private static bool ValidateOpenForSelection()
        {
            return Selection.activeObject is Texture2D;
        }

        private void OnEnable()
        {
            EnsureMaterial();
            LoadChannelFromLinkedShader();
            _hasSample = false;
            if (_texture == null)
            {
                LoadTextureFromLinkedMaterials();
            }
            else
            {
                _pendingMaterialSync = true;
            }
        }

        private void OnDisable()
        {
            if (_previewMaterial != null)
            {
                DestroyImmediate(_previewMaterial);
                _previewMaterial = null;
            }

            if (_samplePixelBuffer != null)
            {
                DestroyImmediate(_samplePixelBuffer);
                _samplePixelBuffer = null;
            }
        }

        private void EnsureMaterial()
        {
            if (_previewMaterial != null)
            {
                return;
            }

            var shader = Shader.Find("Hidden/Editor/ChannelGrayscalePreview");
            if (shader == null)
            {
                Debug.LogError("ChannelGrayscalePreview shader not found. Please reimport 'Assets/Editor/ChannelGrayscalePreview.shader'.");
                return;
            }

            _previewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            var selectedTexture = (Texture2D)EditorGUILayout.ObjectField("Texture", _texture, typeof(Texture2D), false);
            if (!ReferenceEquals(selectedTexture, _texture))
            {
                _texture = selectedTexture;
                _pendingMaterialSync = true;
                _hasSample = false;
            }

            DrawChannelButtons();

            using (new EditorGUILayout.HorizontalScope())
            {
                _useSrgbSampling = EditorGUILayout.ToggleLeft("显示为 sRGB（类似 PS）", _useSrgbSampling);
                if (_channel == Channel.Alpha)
                {
                    _treatAlphaAsMask = EditorGUILayout.ToggleLeft("显示 Alpha 遮罩", _treatAlphaAsMask);
                }
            }

            if (_texture == null)
            {
                EditorGUILayout.HelpBox("Select a texture to preview its channels in grayscale.", MessageType.Info);
                return;
            }

            if (_previewMaterial == null)
            {
                EditorGUILayout.HelpBox("Preview material not ready. See Console for details.", MessageType.Error);
                return;
            }

            var aspect = (float)_texture.width / _texture.height;
            var previewRect = GUILayoutUtility.GetAspectRect(aspect);

            HandlePreviewSampling(previewRect);

            _previewMaterial.SetTexture(MainTexId, _texture);
            _previewMaterial.SetVector(ChannelMaskId, GetChannelMask(_channel));
            _previewMaterial.SetFloat(UseSrgbSamplingId, _useSrgbSampling ? 1f : 0f);
            _previewMaterial.SetFloat(ShowAlphaAsMaskId, _treatAlphaAsMask ? 1f : 0f);

            EditorGUI.DrawPreviewTexture(previewRect, _texture, _previewMaterial, ScaleMode.ScaleToFit);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("小提示", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("• 可以直接从 Project 面板拖拽贴图到此窗口。");
                EditorGUILayout.LabelField("• 关闭 sRGB 选项可查看线性/数据贴图（如法线、遮罩）的原始值。");
                EditorGUILayout.LabelField("• 在预览上点击即可吸取像素灰度值，自动复制到剪贴板。");

                if (_hasSample)
                {
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField("吸管信息", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"UV: {_lastSampleUv.x:F3}, {_lastSampleUv.y:F3}");
                    EditorGUILayout.LabelField($"原始 RGBA: {_lastSampleColor.r:F3}, {_lastSampleColor.g:F3}, {_lastSampleColor.b:F3}, {_lastSampleColor.a:F3}");
                    EditorGUILayout.LabelField($"当前灰度: {_lastSampleValue:F4}");
                }
            }

            if (_pendingMaterialSync && _texture != null)
            {
                SyncLinkedMaterialsTexture();
                _pendingMaterialSync = false;
            }
        }

        private void DrawChannelButtons()
        {
            EditorGUILayout.LabelField("Channel", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawChannelButton("R", Channel.Red, EditorStyles.miniButtonLeft);
                DrawChannelButton("G", Channel.Green, EditorStyles.miniButtonMid);
                DrawChannelButton("B", Channel.Blue, EditorStyles.miniButtonMid);
                DrawChannelButton("A", Channel.Alpha, EditorStyles.miniButtonRight);
            }
        }

        private void DrawChannelButton(string label, Channel channel, GUIStyle style)
        {
            bool selected = _channel == channel;
            bool pressed = GUILayout.Toggle(selected, label, style, GUILayout.Width(40f));

            if (pressed && !selected)
            {
                SetChannel(channel);
            }
        }

        private void SetChannel(Channel channel)
        {
            if (_channel == channel)
            {
                return;
            }

            _channel = channel;
            if (_channel != Channel.Alpha)
            {
                _treatAlphaAsMask = false;
            }

            TrySyncLinkedShader();
        }

        private static Vector4 GetChannelMask(Channel channel)
        {
            switch (channel)
            {
                case Channel.Red:
                    return new Vector4(1f, 0f, 0f, 0f);
                case Channel.Green:
                    return new Vector4(0f, 1f, 0f, 0f);
                case Channel.Blue:
                    return new Vector4(0f, 0f, 1f, 0f);
                case Channel.Alpha:
                    return new Vector4(0f, 0f, 0f, 1f);
                default:
                    return Vector4.zero;
            }
        }

        private void HandlePreviewSampling(Rect previewRect)
        {
            var evt = Event.current;
            if (evt == null || _texture == null)
            {
                return;
            }

            if ((evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag) && evt.button == 0 && previewRect.Contains(evt.mousePosition))
            {
                var local = evt.mousePosition - previewRect.position;
                var uv = new Vector2(
                    Mathf.Clamp01(local.x / Mathf.Max(1f, previewRect.width)),
                    Mathf.Clamp01(1f - (local.y / Mathf.Max(1f, previewRect.height))));

                SamplePixelAtUv(uv);
                evt.Use();
            }
        }

        private void SamplePixelAtUv(Vector2 uv)
        {
            if (_texture == null)
            {
                return;
            }

            var color = ReadTextureColor(_texture, uv);
            _lastSampleUv = uv;
            _lastSampleColor = color;
            _lastSampleValue = ComputeChannelValue(color);
            _hasSample = true;

            EditorGUIUtility.systemCopyBuffer = _lastSampleValue.ToString("F4");
            Repaint();
        }

        private float ComputeChannelValue(Color color)
        {
            var rawRGB = new Vector3(color.r, color.g, color.b);
            var displayRGB = rawRGB;
            if (_useSrgbSampling)
            {
                displayRGB.x = Mathf.LinearToGammaSpace(displayRGB.x);
                displayRGB.y = Mathf.LinearToGammaSpace(displayRGB.y);
                displayRGB.z = Mathf.LinearToGammaSpace(displayRGB.z);
            }

            var mask = GetChannelMask(_channel);
            var data = new Vector4(displayRGB.x, displayRGB.y, displayRGB.z, color.a);
            var value = Vector4.Dot(data, mask);

            if (_channel == Channel.Alpha && _treatAlphaAsMask)
            {
                value *= color.a;
            }

            return Mathf.Clamp01(value);
        }

        private Color ReadTextureColor(Texture texture, Vector2 uv)
        {
            int width = Mathf.Max(1, texture.width);
            int height = Mathf.Max(1, texture.height);

            var tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, tempRT);

            var previous = RenderTexture.active;
            RenderTexture.active = tempRT;

            EnsureSampleBuffer();

            float px = Mathf.Clamp(uv.x * (width - 1), 0f, width - 1f);
            float py = Mathf.Clamp(uv.y * (height - 1), 0f, height - 1f);
            _samplePixelBuffer.ReadPixels(new Rect(px, py, 1f, 1f), 0, 0);
            _samplePixelBuffer.Apply(false, false);
            var color = _samplePixelBuffer.GetPixel(0, 0);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tempRT);

            return color;
        }

        private void EnsureSampleBuffer()
        {
            if (_samplePixelBuffer != null)
            {
                return;
            }

            _samplePixelBuffer = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private void LoadTextureFromLinkedMaterials()
        {
            foreach (var mat in EnumerateLinkedMaterials())
            {
                if (mat == null)
                {
                    continue;
                }

                var tex = mat.GetTexture(LightMapTexId) as Texture2D;
                if (tex != null)
                {
                    _texture = tex;
                    _pendingMaterialSync = false;
                    break;
                }
            }
        }

        private void LoadChannelFromLinkedShader()
        {
            if (!TryReadLinkedShader(out var content, out _))
            {
                return;
            }

            var match = LightMapSampleRegex.Match(content);
            if (!match.Success)
            {
                return;
            }

            var channelToken = match.Groups["channel"].Value;
            if (channelToken.Length != 1)
            {
                return;
            }

            if (TryGetChannelFromSuffix(channelToken[0], out var channel))
            {
                _channel = channel;
                if (_channel != Channel.Alpha)
                {
                    _treatAlphaAsMask = false;
                }
            }
        }

        private void TrySyncLinkedShader()
        {
            if (!TryReadLinkedShader(out var content, out var fullPath))
            {
                return;
            }

            var match = LightMapSampleRegex.Match(content);
            if (!match.Success)
            {
                return;
            }

            var desiredSuffix = GetChannelSuffix(_channel);
            if (string.IsNullOrEmpty(desiredSuffix))
            {
                return;
            }

            var currentSuffix = match.Groups["channel"].Value;
            if (string.Equals(currentSuffix, desiredSuffix, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var updated = LightMapSampleRegex.Replace(
                    content,
                    $"SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, IN.uv).{desiredSuffix}");

                File.WriteAllText(fullPath, updated);
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Channel Viewer: 无法同步 {LinkedShaderAssetPath}，原因：{ex.Message}");
            }
        }

        private void SyncLinkedMaterialsTexture()
        {
            if (_texture == null)
            {
                return;
            }

            bool dirty = false;
            foreach (var mat in EnumerateLinkedMaterials())
            {
                if (mat == null)
                {
                    continue;
                }

                if (!ReferenceEquals(mat.GetTexture(LightMapTexId), _texture))
                {
                    mat.SetTexture(LightMapTexId, _texture);
                    EditorUtility.SetDirty(mat);
                    dirty = true;
                }
            }

            if (dirty)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private static bool TryReadLinkedShader(out string content, out string fullPath)
        {
            content = string.Empty;
            fullPath = string.Empty;

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return false;
            }

            fullPath = Path.Combine(projectRoot, LinkedShaderAssetPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                return false;
            }

            try
            {
                content = File.ReadAllText(fullPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Channel Viewer: 读取 {LinkedShaderAssetPath} 失败，原因：{ex.Message}");
                return false;
            }
        }

        private static bool TryGetChannelFromSuffix(char suffix, out Channel channel)
        {
            switch (suffix)
            {
                case 'r':
                case 'R':
                    channel = Channel.Red;
                    return true;
                case 'g':
                case 'G':
                    channel = Channel.Green;
                    return true;
                case 'b':
                case 'B':
                    channel = Channel.Blue;
                    return true;
                case 'a':
                case 'A':
                    channel = Channel.Alpha;
                    return true;
                default:
                    channel = Channel.Red;
                    return false;
            }
        }

        private static string GetChannelSuffix(Channel channel)
        {
            switch (channel)
            {
                case Channel.Red:
                    return "r";
                case Channel.Green:
                    return "g";
                case Channel.Blue:
                    return "b";
                case Channel.Alpha:
                    return "a";
                default:
                    return string.Empty;
            }
        }

        private static IEnumerable<Material> EnumerateLinkedMaterials()
        {
            var shader = Shader.Find(LinkedShaderName);
            if (shader == null)
            {
                yield break;
            }

            var guids = AssetDatabase.FindAssets("t:Material");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null && mat.shader == shader)
                {
                    yield return mat;
                }
            }
        }
    }
}
