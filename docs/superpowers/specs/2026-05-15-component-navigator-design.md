# Component Navigator — Design Spec
Date: 2026-05-15

## Overview

Extension del Inspector de Unity para GameObjects que añade:
1. **Tab bar**: el GO seleccionado + todos sus hijos directos como tabs
2. **Component button strip**: misma estética que Unity 6 (icono + nombre, ancho uniforme, filas con wrap)
3. **Filter / quick-view**: al togglear un botón, renderiza ese componente inline en el header; la lista nativa queda debajo

### Limitación técnica conocida

`InspectorWindow` siempre renderiza los editores de componentes de forma independiente, debajo del header del GO. No hay API pública para suprimirlos. Por tanto, el "filter" NO oculta el inspector nativo — muestra el componente seleccionado en un panel propio encima del inspector completo. El usuario puede desplegar/cerrar ese panel con el toggle.

Si en el futuro se quiere filtrado real (ocultar componentes del inspector nativo), requeriría reflection sobre `InspectorWindow` internos o UIToolkit tree manipulation — fuera de scope.

---

## Visual Layout

```
┌──────────────────────────────────────────────────────┐
│ [Player] [Weapon] [Shield] [FX]                      │  ← TABS (root GO + direct children)
├──────────────────────────────────────────────────────┤
│ [🔲 Transform] [● Rigidbody*] [■ CapsuleCollider]    │  ← component buttons (active tab's GO)
│ [■ PlayerManager] [■ PlayerAudio] [■ Animator]       │    uniform size, wrapping rows
├──────────────────────────────────────────────────────┤
│ ▼ Rigidbody  ×                                       │  ← quick-view panel (shown when toggled)
│   Mass: 1   Drag: 0   Angular Drag: 0.05             │    "×" cierra el panel
│   Use Gravity: ☑   Is Kinematic: ☐                   │
├──────────────────────────────────────────────────────┤
│ [ Inspector nativo de Unity siempre presente abajo ] │
└──────────────────────────────────────────────────────┘
```

Al seleccionar tab hijo: botones muestran componentes del hijo. Click en componente → quick-view inline del componente del hijo. Sin navegación, sin cambio de selección en Hierarchy.

---

## Arquitectura

### Implementación: `finishedDefaultHeaderGUI` injection

Mantiene la estrategia actual de `ComponentNavigator.cs` — inyección en el header via `Editor.finishedDefaultHeaderGUI`. Más seguro que `[CustomEditor(typeof(GameObject))]`: no interfiere con el inspector nativo, no requiere reimplementar el header del GO.

**No** se usa `[CustomEditor(typeof(GameObject))]` — causaría perder prefab overrides header, static flags, multi-edit UI y potenciales conflictos con otros plugins.

### Clase: `ComponentNavigator` (static, `[InitializeOnLoad]`)

Estado:
```csharp
static int   _selectedTabIndex;      // 0 = root, 1..n = hijo
static int   _activeFilterIndex;     // -1 = ninguno, >=0 = índice en componentes del tab activo
static Editor _quickViewEditor;      // Editor cacheado para el quick-view panel
static int   _lastInstanceId;        // detecta cambio de GO seleccionado → reset estado
```

---

## Section 1: Tab Bar

Tabs = `[root GO name] + [child.name for each direct child]`.

Renderizado: `GUILayout.Toolbar(_selectedTabIndex, tabLabels, EditorStyles.toolbarButton)`.

Al cambiar tab:
- Reset `_activeFilterIndex = -1`
- Destruir y nullear `_quickViewEditor`

Tab list reconstruida cada frame si `go.transform.childCount` cambió desde el frame anterior.

---

## Section 2: Component Button Strip

GO fuente = root si tab 0, else `root.transform.GetChild(_selectedTabIndex - 1).gameObject`.

Para cada componente:
- `icon = EditorGUIUtility.ObjectContent(c, c.GetType()).image`
- `content = new GUIContent(" TypeName", icon)`
- `btnWidth = (EditorGUIUtility.currentViewWidth - 22f) / 4` (4 columnas, mismo ancho)
- `btnHeight = EditorStyles.miniButton.CalcSize(new GUIContent("X")).y * 1.2f` (calculado en runtime, no static field)

Toggle behavior:
- `GUI.Toggle(rect, isActive, content, EditorStyles.miniButton)` con tinte visual cuando activo
- Click mismo botón activo → deselecciona (`_activeFilterIndex = -1`)
- Click diferente → cambia filtro

---

## Section 3: Quick-View Panel

Solo visible cuando `_activeFilterIndex >= 0`.

```
[Header: ComponentName] [× close button]
─────────────────────────────────────────
[component editor via Editor.CreateEditor(component).OnInspectorGUI()]
```

Editor cache: un único `_quickViewEditor`. Cuando cambia el componente filtrado:
```csharp
if (_quickViewEditor != null) DestroyImmediate(_quickViewEditor);
_quickViewEditor = Editor.CreateEditor(targetComponent);
```

Cuando se cierra (× o tab change): `DestroyImmediate(_quickViewEditor); _quickViewEditor = null`.

Envuelto en `try/catch` — si el editor del componente lanza excepción, mostrar HelpBox con el error y continuar.

---

## Reset de estado

Al detectar cambio de GO (`go.GetInstanceID() != _lastInstanceId`):
- Reset todos los campos estáticos
- Destruir `_quickViewEditor`
- Actualizar `_lastInstanceId`

---

## Cleanup

`AssemblyReloadEvents.beforeAssemblyReload` → destruir `_quickViewEditor` para evitar memory leaks.

---

## Out of Scope

- Ocultar componentes del inspector nativo (requiere reflection sobre InspectorWindow)
- Prefab override indicators
- Drag-to-reorder tabs
- Search bar dentro del component strip
- Hijos recursivos (solo hijos directos)
- "Add Component" button (sigue siendo del inspector nativo)
