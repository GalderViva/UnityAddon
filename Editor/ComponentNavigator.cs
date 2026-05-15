using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    [InitializeOnLoad]
    static class ComponentNavigator
    {
        const int Columns = 4;

        static ComponentNavigator()
        {
            Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
        }

        static void OnHeaderGUI(Editor editor)
        {
            if (editor.targets.Length != 1) return;
            if (editor.target is not GameObject go) return;

            var components = go.GetComponents<Component>();
            if (components.Length == 0) return;

            float availableWidth = EditorGUIUtility.currentViewWidth - 22f;
            float btnWidth = availableWidth / Columns;
            float btnHeight = Mathf.Round(EditorStyles.miniButton.CalcSize(new GUIContent("X")).y * 1.2f);

            var items = BuildItems(components);

            EditorGUILayout.Space(2f);
            for (int i = 0; i < items.Count; i += Columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    int end = Mathf.Min(i + Columns, items.Count);
                    for (int j = i; j < end; j++)
                    {
                        var (content, component) = items[j];
                        if (GUILayout.Button(content, EditorStyles.miniButton,
                            GUILayout.Width(btnWidth), GUILayout.Height(btnHeight)))
                        {
                            Selection.activeObject = component;
                            EditorGUIUtility.PingObject(go);
                        }
                    }
                }
            }
            EditorGUILayout.Space(2f);
        }

        static List<(GUIContent content, Component component)> BuildItems(Component[] components)
        {
            var items = new List<(GUIContent, Component)>();
            foreach (var c in components)
            {
                if (c == null) continue;
                var icon = EditorGUIUtility.ObjectContent(c, c.GetType()).image;
                items.Add((new GUIContent($" {c.GetType().Name}", icon), c));
            }
            return items;
        }
    }
}
