using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    public class JsonLogWindow : EditorWindow
    {
        const int MaxEntries = 500;

        struct LogEntry
        {
            public string raw;      // original con <color> tags
            public string display;  // tags stripped
            public LogType type;
            public DateTime timestamp;
        }

        [MenuItem("Tools/Sodium Tools/JSON Log Viewer")]
        static void Open() => GetWindow<JsonLogWindow>("JSON Log Viewer");

        readonly List<LogEntry> _entries = new();
        Vector2 _scroll;
        string _filter = "";
        bool _showLog = true;
        bool _showWarning = true;
        bool _showError = true;
        GUIStyle _richLabel;

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
            Application.logMessageReceived += OnLog;
            _showLog = _showWarning = _showError = true;
        }
        void OnDisable() => Application.logMessageReceived -= OnLog;

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
                GUILayout.Label($"Logs: {_entries.Count}/{MaxEntries}", GUILayout.Width(120));
                GUILayout.Space(4);
                _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
                GUILayout.Space(4);
                _showLog     = GUILayout.Toggle(_showLog,     LogCount(LogType.Log)    + " Log",   EditorStyles.toolbarButton);
                _showWarning = GUILayout.Toggle(_showWarning, LogCount(LogType.Warning) + " Warn",  EditorStyles.toolbarButton);
                _showError   = GUILayout.Toggle(_showError,   LogCount(LogType.Error)   + " Error", EditorStyles.toolbarButton);
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    _entries.Clear();
            }

            var hasFilter = !string.IsNullOrEmpty(_filter);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];

                if (!IsTypeVisible(e.type)) continue;
                if (hasFilter && e.display.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var rowColor = RowColor(e.type);
                var rect = EditorGUILayout.BeginHorizontal();
                if (rowColor.a > 0)
                    EditorGUI.DrawRect(rect, rowColor);

                GUILayout.Label(e.timestamp.ToString("HH:mm:ss"), GUILayout.Width(60));

                if (GUILayout.Button("Open", GUILayout.Width(50)))
                    JsonExpandWindow.Open(e.display);

                var newline = e.raw.IndexOf('\n');
                var firstLine = newline >= 0 ? e.raw.Substring(0, newline) : e.raw;
                float labelW = EditorGUIUtility.currentViewWidth - 68f - 56f;
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
