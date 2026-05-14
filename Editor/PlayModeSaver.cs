using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sodium.Tools
{
    [InitializeOnLoad]
    public static class PlayModeSaver
    {
        static readonly string SavePath =
            Path.Combine(Application.dataPath, "../Library/SodiumToolsSave.json");

        static bool _pendingRestore;

        static PlayModeSaver()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += OnUpdate;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public static void Save(GameObject go)
        {
            if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.Connected)
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                PrefabUtility.ApplyPrefabInstance(root, InteractionMode.UserAction);
                Debug.Log($"[SodiumTools] Prefab overrides applied from {go.name}.");
                return;
            }

            QueueSceneObject(go);
        }

        // ── Serialization ─────────────────────────────────────────────────────

        static void QueueSceneObject(GameObject go)
        {
            var existing = LoadFile();
            var path = HierarchyPath(go);

            var entry = new SaveEntry { path = path, components = new List<SavedComponent>() };
            foreach (var c in go.GetComponents<Component>())
            {
                entry.components.Add(new SavedComponent
                {
                    typeName = c.GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(c, true)
                });
            }

            existing.RemoveAll(e => e.path == path);
            existing.Add(entry);
            WriteFile(existing);

            Debug.Log($"[SodiumTools] Queued {go.name} — restores on Play mode exit.");
        }

        // ── Play mode hooks ───────────────────────────────────────────────────

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                _pendingRestore = true;
        }

        static void OnUpdate()
        {
            if (!_pendingRestore) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            _pendingRestore = false;

            var data = LoadFile();
            if (data.Count == 0) return;

            RestoreAll(data);
        }

        static void RestoreAll(List<SaveEntry> data)
        {
            foreach (var entry in data)
            {
                var go = FindByPath(entry.path);
                if (go == null)
                {
                    Debug.LogWarning($"[SodiumTools] Could not find '{entry.path}' to restore.");
                    continue;
                }

                foreach (var sc in entry.components)
                {
                    var type = Type.GetType(sc.typeName);
                    if (type == null) continue;

                    var c = go.GetComponent(type);
                    if (c == null) c = Undo.AddComponent(go, type);
                    if (c == null) continue;

                    Undo.RecordObject(c, "Restore Play Mode Changes");
                    EditorJsonUtility.FromJsonOverwrite(sc.json, c);
                    EditorUtility.SetDirty(c);
                }

                EditorUtility.SetDirty(go);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }

            DeleteFile();
            Debug.Log("[SodiumTools] Play mode changes restored.");
        }

        // ── Path helpers ──────────────────────────────────────────────────────

        static string HierarchyPath(GameObject go)
        {
            var path = go.name;
            var t = go.transform.parent;
            while (t != null) { path = t.name + "/" + path; t = t.parent; }
            return go.scene.name + "::" + path;
        }

        static GameObject FindByPath(string fullPath)
        {
            var sep = fullPath.IndexOf("::");
            if (sep < 0) return null;

            var sceneName = fullPath.Substring(0, sep);
            var parts = fullPath.Substring(sep + 2).Split('/');

            Scene scene = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.name == sceneName) { scene = s; break; }
            }
            if (!scene.IsValid()) return null;

            GameObject current = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == parts[0]) { current = root; break; }

            for (int i = 1; i < parts.Length && current != null; i++)
            {
                var child = current.transform.Find(parts[i]);
                current = child != null ? child.gameObject : null;
            }

            return current;
        }

        // ── File I/O ──────────────────────────────────────────────────────────

        static List<SaveEntry> LoadFile()
        {
            try
            {
                if (!File.Exists(SavePath)) return new List<SaveEntry>();
                var json = File.ReadAllText(SavePath);
                var wrapper = JsonUtility.FromJson<SaveWrapper>(json);
                return wrapper?.entries ?? new List<SaveEntry>();
            }
            catch { return new List<SaveEntry>(); }
        }

        static void WriteFile(List<SaveEntry> data)
        {
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(new SaveWrapper { entries = data }, true));
            }
            catch (Exception e) { Debug.LogError($"[SodiumTools] Failed to write save file: {e.Message}"); }
        }

        static void DeleteFile()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch { }
        }

        // ── Serializable types ────────────────────────────────────────────────

        [Serializable] public class SavedComponent { public string typeName; public string json; }
        [Serializable] public class SaveEntry { public string path; public List<SavedComponent> components; }
        [Serializable] class SaveWrapper { public List<SaveEntry> entries; }
    }
}
