using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Sodium.Tools
{
    [InitializeOnLoad]
    static class ComponentNavigator
    {
        static int          _selectedTabIndex;
        static int          _activeFilterIndex = -1;
        static int          _lastInstanceId;
        static int          _lastChildCount;
        static string[]     _tabLabels;
        static int[]        _tabDepths;
        static GameObject[] _tabObjects;
        static Editor       _quickViewEditor;
        static Vector2      _tabScroll;
        static string       _tabSearch = string.Empty;

        static readonly GUIContent _copyIcon   = new GUIContent(" Copy",  "Copy All Components");
        static readonly GUIContent _pasteIcon  = new GUIContent(" Paste", "Paste All Components");
        static readonly GUIContent _saveIcon   = new GUIContent(" Save",  "Save in Play Mode");
        static readonly GUIContent _locateIcon = new GUIContent(string.Empty, "Select in Hierarchy");

        static ComponentNavigator()
        {
            Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        static void Cleanup()
        {
            if (_quickViewEditor != null)
                UnityEngine.Object.DestroyImmediate(_quickViewEditor);
            _quickViewEditor = null;
        }

        static void SetAllComponentsExpanded(GameObject go, bool expanded)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null)
                    InternalEditorUtility.SetIsInspectorExpanded(c, expanded);
            ActiveEditorTracker.sharedTracker.ForceRebuild();
        }

        static void RebuildTabs(GameObject go)
        {
            var objects = new List<GameObject> { go };
            var labels  = new List<string>     { go.name };
            var depths  = new List<int>         { 0 };
            CollectDescendants(go.transform, objects, labels, depths, 0);
            _tabObjects = objects.ToArray();
            _tabLabels  = labels.ToArray();
            _tabDepths  = depths.ToArray();
        }

        static void CollectDescendants(Transform parent,
            List<GameObject> objects, List<string> labels, List<int> depths, int depth)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                objects.Add(child.gameObject);
                labels.Add(child.name);
                depths.Add(depth + 1);
                CollectDescendants(child, objects, labels, depths, depth + 1);
            }
        }

        static void OnHeaderGUI(Editor editor)
        {
            if (editor.targets.Length != 1) return;
            if (editor.target is not GameObject go) return;

            int id = go.GetInstanceID();
            if (id != _lastInstanceId || go.transform.childCount != _lastChildCount)
            {
                if (_lastInstanceId != 0 && _tabObjects != null && _tabObjects.Length > 0 && _tabObjects[0] != null)
                    SetAllComponentsExpanded(_tabObjects[0], true);

                _lastInstanceId    = id;
                _lastChildCount    = go.transform.childCount;
                _selectedTabIndex  = 0;
                _activeFilterIndex = -1;
                _tabSearch         = string.Empty;
                Cleanup();
                RebuildTabs(go);
            }

            if (_locateIcon.image == null)
            {
                _copyIcon.image   = EditorGUIUtility.IconContent("TreeEditor.Duplicate").image;
                _pasteIcon.image  = EditorGUIUtility.IconContent("Clipboard").image;
                _saveIcon.image   = EditorGUIUtility.IconContent("d_SaveAs").image
                                 ?? EditorGUIUtility.IconContent("Refresh").image;
                _locateIcon.image = EditorGUIUtility.IconContent("d_HierarchyWindow.SearchByObject").image
                                 ?? EditorGUIUtility.IconContent("SceneAsset Icon").image;
            }

            EditorGUILayout.Space(2f);
            DrawCopyPasteToolbar(go);
            DrawHierarchyList(go);
            EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider);
            DrawComponentStrip(go);
            DrawQuickViewPanel(go);
            EditorGUILayout.Space(2f);
        }

        // ── Toolbar ──────────────────────────────────────────────────────────

        static void DrawCopyPasteToolbar(GameObject rootGo)
        {
            const float btnH = 26f;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(2f);

            if (GUILayout.Button(_copyIcon, GUILayout.Height(btnH)))
                ComponentClipboard.Copy(rootGo);

            using (new EditorGUI.DisabledScope(!ComponentClipboard.HasData))
            {
                if (GUILayout.Button(_pasteIcon, GUILayout.Height(btnH)))
                    ComponentClipboard.Paste(rootGo);
            }

            GUILayout.Space(6f);

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button(_saveIcon, GUILayout.Height(btnH)))
                {
                    bool isPrefab = PrefabUtility.GetPrefabInstanceStatus(rootGo) == PrefabInstanceStatus.Connected;
                    string msg = isPrefab
                        ? $"Aplicar todos los overrides de '{rootGo.name}' al prefab?\n\nEsta acción no se puede deshacer."
                        : $"Guardar '{rootGo.name}' para restaurar al salir del Play Mode?";

                    if (EditorUtility.DisplayDialog("Salvar GameObject", msg, "Salvar", "Cancelar"))
                        PlayModeSaver.Save(rootGo);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        // ── Hierarchy list ───────────────────────────────────────────────────

        static void DrawHierarchyList(GameObject rootGo)
        {
            if (_tabObjects == null || _tabObjects.Length <= 1) return;

            // Search bar
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _tabSearch = EditorGUILayout.TextField(_tabSearch, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                    _tabSearch = string.Empty;
            }

            bool   hasSearch  = !string.IsNullOrEmpty(_tabSearch);
            string searchLow  = hasSearch ? _tabSearch.ToLowerInvariant() : null;

            // Count visible rows for height
            int visible = 0;
            foreach (var lbl in _tabLabels)
                if (!hasSearch || lbl.ToLowerInvariant().Contains(searchLow))
                    visible++;

            float rowH      = 20f;
            float listH     = Mathf.Min(visible * rowH + 2f, 160f);
            var   selColor  = EditorGUIUtility.isProSkin
                ? new Color(0.172f, 0.364f, 0.529f, 1f)
                : new Color(0.24f,  0.49f,  0.91f,  1f);
            var   altColor  = new Color(0f, 0f, 0f, 0.04f);
            var   dotColor  = new Color(0.55f, 0.55f, 0.55f, 0.5f);

            _tabScroll = EditorGUILayout.BeginScrollView(
                _tabScroll, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Height(listH));

            int visIdx = 0;
            for (int i = 0; i < _tabLabels.Length; i++)
            {
                if (hasSearch && !_tabLabels[i].ToLowerInvariant().Contains(searchLow))
                    continue;

                bool isSelected = (_selectedTabIndex == i);
                int  depth      = _tabDepths[i];

                Rect row = GUILayoutUtility.GetRect(0f, rowH, GUILayout.ExpandWidth(true));

                // Background
                if (isSelected)
                    EditorGUI.DrawRect(row, selColor);
                else if (visIdx % 2 == 1)
                    EditorGUI.DrawRect(row, altColor);

                // Locate button — far left
                const float locW = 20f;
                Rect btn = new Rect(row.x, row.y + 1f, locW, rowH - 2f);
                if (GUI.Button(btn, _locateIcon, EditorStyles.iconButton))
                {
                    Selection.activeGameObject = _tabObjects[i];
                    EditorGUIUtility.PingObject(_tabObjects[i]);
                }

                // Depth dots (offset by locW)
                for (int d = 0; d < depth; d++)
                    EditorGUI.DrawRect(
                        new Rect(row.x + locW + d * 14f + 9f, row.y + (rowH - 3f) * 0.5f, 3f, 3f),
                        dotColor);

                // Icon + Label (offset by locW)
                float indent = locW + (depth > 0 ? depth * 14f + 16f : 4f);
                var   goIcon = EditorGUIUtility.ObjectContent(_tabObjects[i], typeof(GameObject)).image;
                if (goIcon != null)
                    GUI.DrawTexture(new Rect(row.x + indent, row.y + 2f, 16f, 16f),
                                   goIcon, ScaleMode.ScaleToFit);
                float textX = row.x + indent + (goIcon != null ? 18f : 0f);
                var   lStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                if (isSelected) lStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(textX, row.y, row.xMax - textX, row.height),
                          _tabLabels[i], lStyle);

                // Row click (select)
                if (Event.current.type == EventType.MouseDown
                    && Event.current.button == 0
                    && row.Contains(Event.current.mousePosition)
                    && !btn.Contains(Event.current.mousePosition))
                {
                    if (_activeFilterIndex >= 0)
                        SetAllComponentsExpanded(rootGo, true);
                    _selectedTabIndex  = i;
                    _activeFilterIndex = -1;
                    Cleanup();
                    Event.current.Use();
                    GUI.changed = true;
                }

                visIdx++;
            }

            EditorGUILayout.EndScrollView();
        }

        // ── Component strip ──────────────────────────────────────────────────

        static void DrawComponentStrip(GameObject rootGo)
        {
            if (_tabObjects == null || _selectedTabIndex >= _tabObjects.Length) return;
            var sourceGo = _tabObjects[_selectedTabIndex];
            if (sourceGo == null) return;

            var   components     = sourceGo.GetComponents<Component>();
            float availableWidth = EditorGUIUtility.currentViewWidth - 22f;
            float btnWidth       = availableWidth / 4f;
            float btnHeight      = EditorStyles.miniButton.CalcSize(new GUIContent("X")).y * 1.2f;

            int col = 0;
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;

                var  icon     = EditorGUIUtility.ObjectContent(c, c.GetType()).image;
                var  content  = new GUIContent($" {c.GetType().Name}", icon);
                bool isActive = (_activeFilterIndex == i);

                var style = new GUIStyle(EditorStyles.miniButton);
                if (isActive)
                {
                    style.normal.background = style.active.background;
                    style.normal.textColor  = style.active.textColor;
                }

                if (col > 0 && col % 4 == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                if (GUILayout.Button(content, style, GUILayout.Width(btnWidth), GUILayout.Height(btnHeight)))
                {
                    if (isActive)
                    {
                        _activeFilterIndex = -1;
                        Cleanup();
                        SetAllComponentsExpanded(rootGo, true);
                    }
                    else
                    {
                        _activeFilterIndex = i;
                        Cleanup();
                        _quickViewEditor = Editor.CreateEditor(c);
                        SetAllComponentsExpanded(rootGo, false);
                    }
                }
                col++;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Quick-view panel ─────────────────────────────────────────────────

        static void DrawQuickViewPanel(GameObject rootGo)
        {
            if (_quickViewEditor == null) return;

            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField(
                    _quickViewEditor.target.GetType().Name,
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                {
                    _activeFilterIndex = -1;
                    Cleanup();
                    SetAllComponentsExpanded(rootGo, true);
                    return;
                }
            }

            try   { _quickViewEditor.OnInspectorGUI(); }
            catch (Exception e)
            { EditorGUILayout.HelpBox($"Error rendering component: {e.Message}", MessageType.Error); }
        }
    }
}
