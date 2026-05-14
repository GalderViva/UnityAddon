using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    public static class ComponentClipboard
    {
        static readonly List<(Type type, string json)> _data = new();

        public static bool HasData => _data.Count > 0;

        public static void Copy(GameObject go)
        {
            _data.Clear();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c is Transform) continue;
                _data.Add((c.GetType(), EditorJsonUtility.ToJson(c)));
            }
        }

        public static void Paste(GameObject go)
        {
            Undo.RegisterCompleteObjectUndo(go, "Paste All Components");

            foreach (var (type, json) in _data)
            {
                var existing = go.GetComponent(type);
                if (existing == null)
                    existing = Undo.AddComponent(go, type);

                if (existing != null)
                    EditorJsonUtility.FromJsonOverwrite(json, existing);
                else
                    Debug.LogWarning($"[SodiumTools] Could not add component: {type.Name}");
            }
        }
    }
}
