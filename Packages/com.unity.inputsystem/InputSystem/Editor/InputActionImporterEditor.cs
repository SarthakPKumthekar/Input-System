#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Experimental.AssetImporters;
using UnityEngine;

////TODO: replace "Apply" text on button with "Save"

////TODO: create custom editor for InputActionReference which prevents modifying the references

////FIXME: because of how .inputactions are structured, an asset with just a set and no actions in it will come out
////       as no set at all when deserialized and then cause exception in InputActionTreeView

namespace ISX.Editor
{
    // Custom editor that allows modifying importer settings for an InputActionImporter.
    //
    // NOTE: This inspector has an unusual setup in that it not only modifies import settings
    //       but actually overwrites the source file on apply if there have been changes made to the
    //       action sets.
    //
    // NOTE: Depends on InputActionAssetEditor as the chosen editor for the imported asset.
    [CustomEditor(typeof(InputActionImporter))]
    public class InputActionImporterEditor : ScriptedImporterEditor
    {
        [SerializeField] private bool m_AssetIsDirty;
        // We need to be able to revert edits. We support that by simply keeping a copy of
        // the last JSON version of the asset around.
        [SerializeField] private string m_Backup;
        [NonSerialized] private bool m_Initialized;

        protected InputActionAsset GetAsset()
        {
            var asset = (InputActionAsset)assetTarget;
            if (asset == null)
                throw new InvalidOperationException("Asset editor has not been initialized yet");
            return asset;
        }

        protected InputActionAssetEditor GetAssetEditor()
        {
            return InputActionAssetEditor.FindFor(GetAsset());
        }

        protected string GetAssetPath()
        {
            return AssetDatabase.GetAssetPath(GetAsset());
        }

        protected override void Apply()
        {
            RegenerateJsonSourceFile();
            m_AssetIsDirty = false;
            base.Apply();
        }

        // Re-generate the JSON source file and if it doesn't match what's already in
        // the file, overwrite the source file.
        private void RegenerateJsonSourceFile()
        {
            var assetPath = GetAssetPath();
            if (string.IsNullOrEmpty(assetPath))
                return;

            ////REVIEW: can we somehow get pretty-printed JSON instead of the compact form that JsonUtility writes?
            var newJson = GetAsset().ToJson();
            var existingJson = File.ReadAllText(assetPath);

            if (newJson != existingJson)
                File.WriteAllText(assetPath, newJson);

            // Becomes our new backup copy.
            m_Backup = newJson;
        }

        // NOTE: This is called during Awake() when nothing of the asset editing
        //       structure has been initialized yet.
        protected override void ResetValues()
        {
            base.ResetValues();
            m_AssetIsDirty = false;

            // ResetValues() also gets called from the apply logic at a time
            // when the asset editor isn't set up yet.
            var assetObject = (InputActionAsset)assetTarget;
            if (assetObject != null)
            {
                if (m_Backup != null)
                    assetObject.LoadFromJson(m_Backup);

                var editor = InputActionAssetEditor.FindFor(assetObject);
                if (editor != null)
                    editor.Reload();
            }
        }

        public override void OnInspectorGUI()
        {
            // 'assetEditor' is set only after the editor is enabled so do the
            // initialization here.
            if (!m_Initialized)
            {
                GetAssetEditor().m_ApplyAction = OnAssetModified;

                // Read current asset as backup.
                if (m_Backup == null)
                    m_Backup = GetAsset().ToJson();

                m_Initialized = true;
            }

            // Look up properties on importer object.
            var generateWapperCodeProperty = serializedObject.FindProperty("m_GenerateWrapperCode");
            var wrapperCodePathProperty = serializedObject.FindProperty("m_WrapperCodePath");
            var wrapperCodeNamespaceProperty = serializedObject.FindProperty("m_WrapperCodeNamespace");

            // Add settings UI.
            EditorGUILayout.PropertyField(generateWapperCodeProperty, Contents.generateWrapperCode);
            if (generateWapperCodeProperty.boolValue)
            {
                ////TODO: tie a file selector to this
                EditorGUILayout.PropertyField(wrapperCodePathProperty);
                EditorGUILayout.PropertyField(wrapperCodeNamespaceProperty);
            }

            ApplyRevertGUI();
        }

        public override bool HasModified()
        {
            return m_AssetIsDirty || base.HasModified();
        }

        private void OnAssetModified()
        {
            m_AssetIsDirty = true;
            Repaint();
        }

        private static class Contents
        {
            public static GUIContent generateWrapperCode = new GUIContent("Generate C# Wrapper Class");
        }
    }
}
#endif // UNITY_EDITOR
