# Unity DevTools Addon — Design Spec
Date: 2026-05-14

## Overview

UPM package (`com.redvel.devtools`) for Unity 6 with three editor utilities:
1. Save prefab overrides during Play mode from the Inspector
2. Copy/paste all components between GameObjects
3. JSON log viewer that captures console output and opens full JSON in a popup

---

## Package Structure

```
Packages/com.redvel.devtools/
├── package.json
└── Editor/
    ├── DevTools.Editor.asmdef
    ├── GameObjectToolsWindow.cs   — EditorWindow for features 1 & 2
    ├── ComponentClipboard.cs      — static logic for component copy/paste
    ├── PrefabSaver.cs             — static logic for Play mode prefab save
    ├── JsonLogWindow.cs           — EditorWindow: log capture + JSON detection
    └── JsonExpandWindow.cs        — EditorWindow: full JSON viewer popup
```

Menu paths:
- `Tools > RedVel DevTools > GameObject Tools`
- `Tools > RedVel DevTools > JSON Log Viewer`

---

## Feature 1: Save Prefab in Play Mode

**Location:** `GameObjectToolsWindow` (dockable EditorWindow)

**Behavior:**
- Button "Save to Prefab" is visible only when:
  - `EditorApplication.isPlaying == true`
  - A GameObject is selected
  - `PrefabUtility.IsPartOfPrefabInstance(selectedGO) == true`
- On click: calls `PrefabUtility.ApplyAllOverrides(prefabRoot, InteractionMode.UserAction)`
  where `prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(selectedGO)`
- Shows success/error message in the window after apply

**Edge cases:**
- No GameObject selected → button hidden
- Selected GO is not a prefab instance → button hidden
- Not in Play mode → button hidden (edit mode already has native overrides UI)

---

## Feature 2: Copy/Paste All Components

**Location:** `GameObjectToolsWindow` (same window as Feature 1)

**Behavior:**
- "Copy Components" button: iterates `selectedGO.GetComponents<Component>()`, skips `Transform`, serializes each with `EditorJsonUtility.ToJson()`, stores in `ComponentClipboard` static field along with component type names
- "Paste Components" button (enabled only when clipboard has data):
  - For each stored component type: find existing on target GO or add new one
  - Apply stored JSON via `EditorJsonUtility.FromJsonOverwrite(json, component)`
  - Skips `Transform` (position/rotation/scale unchanged)
  - Registers undo via `Undo.RegisterCompleteObjectUndo` before paste

**Edge cases:**
- Clipboard empty → "Paste Components" button disabled
- Component type not found in project → skip with warning logged
- Pasting onto same GO that was copied from → allowed (acts as reset)

---

## Feature 3: JSON Log Viewer

**Location:** `JsonLogWindow` (independent EditorWindow) + `JsonExpandWindow` (popup)

### JsonLogWindow

**Behavior:**
- Subscribes to `Application.logMessageReceived` on `OnEnable`, unsubscribes on `OnDisable`
- Stores up to 500 entries in `List<LogEntry>` (circular — oldest dropped when full)
- Each `LogEntry`: `{ string message, string stackTrace, LogType type, DateTime timestamp }`
- JSON detection: `message.TrimStart()` starts with `{` or `[`
- Renders scrollable list; each entry shows timestamp, log type icon, first 80 chars of message
- JSON entries get an "Open" button next to them
- "Clear" button at top clears the list

### JsonExpandWindow

- Opens as a floating `EditorWindow` via `GetWindow<JsonExpandWindow>()`
- Receives full JSON string
- Attempts pretty-print via `JsonUtility` or manual indent (fallback: raw string)
- Displays in scrollable `EditorGUILayout.TextArea` (read-only style)
- "Copy to Clipboard" button via `EditorGUIUtility.systemCopyBuffer`
- Window title shows first 40 chars of JSON

---

## Data Flow

```
Application.logMessageReceived
    → JsonLogWindow stores LogEntry
    → OnGUI renders list
    → user clicks "Open" on JSON entry
    → JsonExpandWindow.Open(fullJson) called
    → JsonExpandWindow renders full text
```

```
User selects GameObject in Hierarchy
    → GameObjectToolsWindow.OnSelectionChange() fires
    → refreshes displayed GO name and button visibility

User clicks "Copy Components"
    → ComponentClipboard.Copy(go) called
    → serializes all non-Transform components

User selects target GO, clicks "Paste Components"
    → ComponentClipboard.Paste(go) called
    → applies stored component data with Undo support
```

---

## Out of Scope

- Pretty-printing deeply nested JSON (basic indent only, no syntax highlighting)
- Saving component clipboard across sessions (in-memory only, lost on domain reload)
- Partial component selection for copy/paste
- Filtering/searching in JsonLogWindow
