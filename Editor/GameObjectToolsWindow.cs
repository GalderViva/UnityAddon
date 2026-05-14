using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    public class GameObjectToolsWindow : EditorWindow
    {
        [MenuItem("Tools/Sodium Tools/GameObject Tools")]
        static void Open() => GetWindow<GameObjectToolsWindow>("GameObject Tools");

        GameObject _selected;
        string _statusMessage;

        void OnSelectionChange()
        {
            _selected = Selection.activeGameObject;
            _statusMessage = null;
            Repaint();
        }

        void OnInspectorUpdate() => Repaint();

        void OnGUI()
        {
            if (_selected == null)
            {
                EditorGUILayout.HelpBox("Select a GameObject in the Hierarchy.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(_selected.name, EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawPrefabSaver();
            EditorGUILayout.Space(8);
            DrawComponentCopier();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }

        void DrawPrefabSaver()
        {
            EditorGUILayout.LabelField("Save", EditorStyles.miniBoldLabel);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to save changes.", MessageType.None);
                return;
            }

            if (GUILayout.Button("Save GameObject"))
            {
                PlayModeSaver.Save(_selected);
                _statusMessage = "Saved. Prefabs apply now; scene objects restore on Play mode exit.";
            }
        }

        void DrawComponentCopier()
        {
            EditorGUILayout.LabelField("Components", EditorStyles.miniBoldLabel);

            if (GUILayout.Button("Copy All Components"))
            {
                ComponentClipboard.Copy(_selected);
                _statusMessage = $"Copied components from {_selected.name}.";
            }

            using (new EditorGUI.DisabledScope(!ComponentClipboard.HasData))
            {
                if (GUILayout.Button("Paste All Components"))
                {
                    ComponentClipboard.Paste(_selected);
                    _statusMessage = $"Pasted components onto {_selected.name}.";
                }
            }
        }
    }
}
