using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Exerussus.Outline.Rendering;

namespace Exerussus.Outline.Editor
{
    /// <summary>Добавляет OutlineRendererFeature во все Universal Renderer Data проекта и проставляет шейдеры.</summary>
    public static class OutlineFeatureInstaller
    {
        [MenuItem("Exerussus/Outline/Подключить к рендерерам URP")]
        public static void InstallMenu()
        {
            int added = Install(out _);
            Debug.Log($"[Outline] Фича подключена, новых: {added}.");
        }

        /// <summary>
        /// Подключить фичу ко всем UniversalRendererData в Assets. Возвращает число новых подключений;
        /// firstFeature — фича первого рендерера (приоритет у имени с «PC_»).
        /// </summary>
        public static int Install(out OutlineRendererFeature firstFeature)
        {
            firstFeature = null;
            var mask = Shader.Find(OutlineRendererFeature.MaskShaderName);
            var jfa = Shader.Find(OutlineRendererFeature.JumpFloodShaderName);
            var comp = Shader.Find(OutlineRendererFeature.CompositeShaderName);

            int added = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/"))
                    continue;
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null)
                    continue;

                OutlineRendererFeature feature = null;
                foreach (var f in data.rendererFeatures)
                    if (f is OutlineRendererFeature of)
                        feature = of;

                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<OutlineRendererFeature>();
                    feature.name = "ExerussusOutline";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

                    var so = new SerializedObject(data);
                    var features = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    features.arraySize++;
                    features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    added++;
                }

                var fso = new SerializedObject(feature);
                fso.FindProperty("maskShader").objectReferenceValue = mask;
                fso.FindProperty("jumpFloodShader").objectReferenceValue = jfa;
                fso.FindProperty("compositeShader").objectReferenceValue = comp;
                fso.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(data);
                data.SetDirty();
                if (firstFeature == null || path.Contains("PC_"))
                    firstFeature = feature;
            }
            AssetDatabase.SaveAssets();
            return added;
        }
    }
}
