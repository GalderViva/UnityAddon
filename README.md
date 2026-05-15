# Sodium Tools

Unity 6 editor utilities that extend the Inspector and add a smarter console.

## Install

**Window → Package Manager → `+` → Add package from git URL:**

```
https://github.com/GalderVivas/UnityAddon.git
```

## Features

### Component Navigator

Injected directly into the Unity Inspector. Select any GameObject to get:

- **Hierarchy list** — all descendants (children, grandchildren, etc.) in a scrollable panel with depth indentation, search filter, and a locate button per row
- **Component strip** — components of the selected row shown as uniform icon+name buttons (4 columns)
- **Quick-view filter** — click a component button to collapse all others and show that component's inspector inline; click again or `×` to restore
- **Copy / Paste / Save** — toolbar buttons to copy all components, paste onto another GO, or save Play Mode changes

### Play Mode Saver

- **Prefab instances** — applies all overrides to the prefab asset (permanent)
- **Scene objects** — queues component data and restores it when you exit Play Mode
- Confirmation dialog before saving

### Component Clipboard

Copy all components from one GameObject and paste them onto another. Skips Transform. Supports Undo.

### Sodium Console

A dockable log console that replaces the noise of Unity's default console:

- **Collapse** — groups identical messages with a count badge (on by default)
- **Filter** — search bar + Log / Warn / Error toggles with color coding
- **Open** — click any entry to view the full message in a popup (useful for JSON logs)
- Persists between Unity sessions — reopens automatically on editor load

## Requirements

- Unity 6000.0+

## License

MIT
