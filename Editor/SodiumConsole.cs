using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    [InitializeOnLoad]
    static class SodiumConsoleRestorer
    {
        static SodiumConsoleRestorer()
        {
            if (EditorPrefs.GetBool(SodiumConsole.PrefKey, false))
                EditorApplication.update += Reopen;
        }

        static void Reopen()
        {
            EditorApplication.update -= Reopen;
            EditorWindow.GetWindow<SodiumConsole>();
        }
    }

    public class SodiumConsole : EditorWindow
    {
        internal const string PrefKey = "Sodium.ConsoleOpen";

        const int MaxEntries = 500;

        struct LogEntry
        {
            public string raw;
            public string display;
            public LogType type;
            public DateTime timestamp;
        }

        [MenuItem("Tools/Sodium Tools/Sodium Console")]
        static void Open() => GetWindow<SodiumConsole>();

        readonly List<LogEntry> _entries = new();
        Vector2 _scroll;
        string _filter   = "";
        bool _showLog    = true;
        bool _showWarning = true;
        bool _showError  = true;
        bool _collapse   = true;
        GUIStyle _richLabel;
        GUIStyle _badgeStyle;

        GUIStyle RichLabel
        {
            get
            {
                if (_richLabel == null)
                {
                    _richLabel = new GUIStyle(EditorStyles.label)
                    {
                        richText = true,
                        wordWrap = false,
                        clipping = TextClipping.Clip
                    };
                }
                return _richLabel;
            }
        }

        void OnEnable()
        {
            var icon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image;
            titleContent = new GUIContent("Sodium Console", icon);
            Application.logMessageReceived += OnLog;
            _showLog = _showWarning = _showError = true;
            EditorPrefs.SetBool(PrefKey, true);
        }

        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnDestroy() => EditorPrefs.SetBool(PrefKey, false);

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (_entries.Count >= MaxEntries)
                _entries.RemoveAt(0);

            _entries.Add(new LogEntry
            {
                raw = message,
                display = StripTags(message),
                type = type,
                timestamp = DateTime.Now
            });

            Repaint();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"Logs: {_entries.Count}/{MaxEntries}", GUILayout.Width(130));
                GUILayout.Space(4);
                _filter   = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
                GUILayout.Space(4);
                _collapse = GUILayout.Toggle(_collapse, "Collapse", EditorStyles.toolbarButton);

                GUI.contentColor = new Color(0.7f, 0.9f, 1f);
                _showLog     = GUILayout.Toggle(_showLog,     LogCount(LogType.Log)     + " Log",   EditorStyles.toolbarButton);
                GUI.contentColor = new Color(1f, 0.85f, 0.25f);
                _showWarning = GUILayout.Toggle(_showWarning, LogCount(LogType.Warning) + " Warn",  EditorStyles.toolbarButton);
                GUI.contentColor = new Color(1f, 0.4f, 0.4f);
                _showError   = GUILayout.Toggle(_showError,   LogCount(LogType.Error)   + " Error", EditorStyles.toolbarButton);
                GUI.contentColor = Color.white;
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    _entries.Clear();
            }

            var hasFilter = !string.IsNullOrEmpty(_filter);

            if (_badgeStyle == null)
                _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = Color.white },
                    fontSize  = 9
                };

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Build collapsed view: last entry + count per unique message
            var seen   = new Dictionary<string, int>();
            var toShow = new List<(LogEntry entry, int count)>();

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (!IsTypeVisible(e.type)) continue;
                if (hasFilter && e.display.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (_collapse)
                {
                    if (!seen.ContainsKey(e.display))
                    {
                        seen[e.display] = 1;
                        toShow.Add((e, 1));
                    }
                    else
                    {
                        seen[e.display]++;
                        // update count in existing entry
                        for (int j = 0; j < toShow.Count; j++)
                        {
                            if (toShow[j].entry.display == e.display)
                            {
                                toShow[j] = (toShow[j].entry, seen[e.display]);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    toShow.Add((e, 1));
                }
            }

            foreach (var (e, count) in toShow)
            {
                var rowColor = RowColor(e.type);
                var rect     = EditorGUILayout.BeginHorizontal();
                if (rowColor.a > 0) EditorGUI.DrawRect(rect, rowColor);

                GUILayout.Label(e.timestamp.ToString("HH:mm:ss"), GUILayout.Width(60));

                if (_collapse && count > 1)
                {
                    const float badgeW = 36f;
                    var badgeRect = GUILayoutUtility.GetRect(badgeW, 18f, GUILayout.Width(badgeW));
                    badgeRect.y      += 1f;
                    badgeRect.height -= 2f;
                    EditorGUI.DrawRect(badgeRect, new Color(0.18f, 0.48f, 0.9f, 1f));
                    GUI.Label(badgeRect, count > 999 ? "999+" : count.ToString(), _badgeStyle);
                }
                else if (_collapse)
                {
                    GUILayout.Space(36f);
                }

                if (GUILayout.Button("Open", GUILayout.Width(50)))
                    JsonExpandWindow.Open(e.display);

                var newline   = e.raw.IndexOf('\n');
                var firstLine = newline >= 0 ? e.raw.Substring(0, newline) : e.raw;
                float labelW  = EditorGUIUtility.currentViewWidth - 68f - 56f - 40f;
                GUILayout.Label(firstLine, RichLabel, GUILayout.Width(Mathf.Max(0, labelW)));

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        bool IsTypeVisible(LogType type) => type switch
        {
            LogType.Warning => _showWarning,
            LogType.Error or LogType.Exception or LogType.Assert => _showError,
            _ => _showLog
        };

        int LogCount(LogType type) => _entries.FindAll(e =>
            type == LogType.Log
                ? e.type == LogType.Log
                : type == LogType.Warning
                    ? e.type == LogType.Warning
                    : e.type == LogType.Error || e.type == LogType.Exception || e.type == LogType.Assert
        ).Count;

        static Color RowColor(LogType type) => type switch
        {
            LogType.Error or LogType.Exception or LogType.Assert => new Color(1f, 0.3f, 0.3f, 0.15f),
            LogType.Warning => new Color(1f, 0.85f, 0.3f, 0.12f),
            _ => Color.clear
        };

        static string StripTags(string s) => Regex.Replace(s, "<[^>]+>", "");
    }
}
