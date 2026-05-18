# Component Navigator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewrite `ComponentNavigator.cs` to show a tab bar (root GO + direct children) with a wrapping component button strip that quick-views any component inline when toggled.

**Architecture:** Static `[InitializeOnLoad]` class injecting into `Editor.finishedDefaultHeaderGUI`. All state is static (one active inspector at a time). Tab bar uses `GUILayout.Toolbar`. Quick-view renders a cached `Editor.CreateEditor(component)` inline. No `[CustomEditor(typeof(GameObject))]` — keeps Unity's native inspector intact.

**Tech Stack:** Unity 6 Editor (IMGUI), `Editor.finishedDefaultHeaderGUI`, `AssemblyReloadEvents`, `GUILayout.Toolbar`, `Editor.CreateEditor`

---

### Task 1: State fields, GO-change detection, tab rebuild

**Files:**
- Modify: `Editor/ComponentNavigator.cs` (full rewrite)

- [ ] **Step 1: Replace entire file with skeleton + state + tab rebuild**

```csharp
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    [InitializeOnLoad]
    static class ComponentNavigator
    {
        static int      _selectedTabIndex;
        static int      _activeFilterIndex = -1;
        static int      _lastInstanceId;
        static int      _lastChildCount;
        static string[] _tabLabels;
        static GameObject[] _tabObjects;
        static Editor   _quickViewEditor;

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

        static void RebuildTabs(GameObject go)
        {
            int childCount = go.transform.childCount;
            _tabObjects = new GameObject[1 + childCount];
            _tabLabels  = new string[1 + childCount];
            _tabObjects[0] = go;
            _tabLabels[0]  = go.name;
            for (int i = 0; i < childCount; i++)
            {
                _tabObjects[i + 1] = go.transform.GetChild(i).gameObject;
                _tabLabels[i + 1]  = go.transform.GetChild(i).name;
            }
        }

        static void OnHeaderGUI(Editor editor)
        {
            if (editor.targets.Length != 1) return;
            if (editor.target is not GameObject go) return;

            int id = go.GetInstanceID();
            if (id != _lastInstanceId || go.transform.childCount != _lastChildCount)
            {
                _lastInstanceId  = id;
                _lastChildCount  = go.transform.childCount;
                _selectedTabIndex = 0;
                _activeFilterIndex = -1;
                Cleanup();
                RebuildTabs(go);
            }

            EditorGUILayout.Space(2f);
            // DrawTabBar, DrawComponentStrip, DrawQuickViewPanel come in later tasks
            EditorGUILayout.Space(2f);
        }
    }
}
```

- [ ] **Step 2: Open Unity, select any GO. No errors in Console. No visual change yet (placeholder spaces only).**

- [ ] **Step 3: Commit**

```
git add Editor/ComponentNavigator.cs
git commit -m "feat: component navigator state + tab rebuild skeleton"
```

---

### Task 2: Tab bar

**Files:**
- Modify: `Editor/ComponentNavigator.cs`

- [ ] **Step 1: Add `DrawTabBar()` method and call it in `OnHeaderGUI`**

Add after `RebuildTabs`:

```csharp
static void DrawTabBar()
{
    if (_tabLabels == null || _tabLabels.Length <= 1) return;

    int newTab = GUILayout.Toolbar(_selectedTabIndex, _tabLabels, EditorStyles.toolbarButton);
    if (newTab != _selectedTabIndex)
    {
        _selectedTabIndex  = newTab;
        _activeFilterIndex = -1;
        Cleanup();
    }
}
```

Replace the two `Space` lines in `OnHeaderGUI` with:

```csharp
EditorGUILayout.Space(2f);
DrawTabBar();
EditorGUILayout.Space(2f);
```

- [ ] **Step 2: In Unity, select a GO with children. Tab bar appears with root name + children names. Switching tabs clears no visual yet (component strip not added). No Console errors.**

- [ ] **Step 3: Commit**

```
git add Editor/ComponentNavigator.cs
git commit -m "feat: component navigator tab bar"
```

---

### Task 3: Component button strip (uniform size, icon, wrap)

**Files:**
- Modify: `Editor/ComponentNavigator.cs`

- [ ] **Step 1: Add `DrawComponentStrip()` method**

```csharp
static void DrawComponentStrip()
{
    if (_tabObjects == null || _selectedTabIndex >= _tabObjects.Length) return;
    var sourceGo = _tabObjects[_selectedTabIndex];
    if (sourceGo == null) return;

    var components = sourceGo.GetComponents<Component>();
    float availableWidth = EditorGUIUtility.currentViewWidth - 22f;
    float btnWidth  = availableWidth / 4f;
    float btnHeight = EditorStyles.miniButton.CalcSize(new GUIContent("X")).y * 1.2f;

    int col = 0;
    EditorGUILayout.BeginHorizontal();
    for (int i = 0; i < components.Length; i++)
    {
        var c = components[i];
        if (c == null) continue;

        var icon    = EditorGUIUtility.ObjectContent(c, c.GetType()).image;
        var content = new GUIContent($" {c.GetType().Name}", icon);
        bool isActive = (_activeFilterIndex == i);

        var style = new GUIStyle(EditorStyles.miniButton);
        if (isActive)
        {
            style.normal.background  = style.active.background;
            style.normal.textColor   = style.active.textColor;
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
            }
            else
            {
                _activeFilterIndex = i;
                Cleanup();
                _quickViewEditor = Editor.CreateEditor(c);
            }
        }
        col++;
    }
    EditorGUILayout.EndHorizontal();
}
```

- [ ] **Step 2: Wire into `OnHeaderGUI` — replace the Space block with:**

```csharp
EditorGUILayout.Space(2f);
DrawTabBar();
DrawComponentStrip();
EditorGUILayout.Space(2f);
```

- [ ] **Step 3: In Unity, select GO. Component buttons appear, 4 per row, uniform width, with icons. Switching tabs shows different GO's components. Clicking a button highlights it (pressed style). Click again → deselects. No errors.**

- [ ] **Step 4: Commit**

```
git add Editor/ComponentNavigator.cs
git commit -m "feat: component navigator button strip with toggle"
```

---

### Task 4: Quick-view panel

**Files:**
- Modify: `Editor/ComponentNavigator.cs`

- [ ] **Step 1: Add `DrawQuickViewPanel()` method**

```csharp
static void DrawQuickViewPanel()
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
            return;
        }
    }

    try
    {
        _quickViewEditor.OnInspectorGUI();
    }
    catch (Exception e)
    {
        EditorGUILayout.HelpBox($"Error rendering component: {e.Message}", MessageType.Error);
    }
}
```

- [ ] **Step 2: Wire into `OnHeaderGUI`:**

```csharp
EditorGUILayout.Space(2f);
DrawTabBar();
DrawComponentStrip();
DrawQuickViewPanel();
EditorGUILayout.Space(2f);
```

- [ ] **Step 3: In Unity:**
  - Select a GO → click a component button → quick-view panel appears below the strip showing that component's inspector fields
  - Click `×` → panel closes, button deselects
  - Click same button again → panel closes
  - Switch tabs → panel closes
  - Select different GO → panel closes and resets
  - Verify: no memory leaks (domain reload in Unity clears the editor cleanly)

- [ ] **Step 4: Commit**

```
git add Editor/ComponentNavigator.cs
git commit -m "feat: component navigator quick-view panel with close button"
```

---

### Task 5: Delete old ComponentNavigator.meta if needed + final smoke test

**Files:**
- Verify: `Editor/ComponentNavigator.cs` compiles cleanly

- [ ] **Step 1: In Unity Console — zero errors, zero warnings from `ComponentNavigator.cs`**

- [ ] **Step 2: Smoke test checklist**
  - GO with no children: tab bar hidden, component strip shows, quick-view works
  - GO with 3 children: tab bar shows 4 tabs, each tab shows correct child's components
  - Child tab → click component → quick-view shows child's component fields, no Hierarchy selection change
  - Resize Inspector window → buttons reflow correctly (4 per row, fill width)
  - Enter/Exit Play mode → state resets cleanly, no null ref errors

- [ ] **Step 3: Final commit**

```
git add Editor/ComponentNavigator.cs
git commit -m "feat: component navigator — tabs + strip + quick-view complete"
```
