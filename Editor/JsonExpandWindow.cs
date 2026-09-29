using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    // Text is drawn line by line with a custom selection instead of a SelectableLabel:
    // IMGUI text controls can't place the selection past ~16384 chars of their string
    // (rich-text tags included), which broke fragment copying deep in long logs.
    public class JsonExpandWindow : EditorWindow
    {
        struct TextPos
        {
            public int Line, Col;
            public TextPos(int line, int col) { Line = line; Col = col; }
            public static bool operator <(TextPos a, TextPos b) => a.Line < b.Line || (a.Line == b.Line && a.Col < b.Col);
            public static bool operator >(TextPos a, TextPos b) => b < a;
        }

        static readonly int TextControlHash = "JsonExpandWindowText".GetHashCode();

        string _raw;
        string[] _lines;         // plain text per line, what gets copied
        string[] _coloredLines;  // rich-text per line (null entry = draw plain), null when not JSON
        string _stackTrace;
        Vector2 _scroll;
        GUIStyle _linkStyle;
        GUIStyle _richTextStyle;
        GUIStyle _plainTextStyle;
        readonly GUIContent _tmp = new GUIContent();

        float _textWidth = -1f;
        float _viewHeight;
        TextPos _anchor, _caret;
        bool _dragging;
        Vector2 _dragViewPos;    // mouse in scroll-viewport coords, used for auto-scroll
        double _lastUpdate;

        static JsonExpandWindow _instance;

        GUIStyle LinkStyle
        {
            get
            {
                if (_linkStyle == null)
                    _linkStyle = new GUIStyle(EditorStyles.label)
                    {
                        normal = { textColor = new Color(0.35f, 0.65f, 1f) },
                        hover  = { textColor = new Color(0.55f, 0.8f, 1f) },
                        active = { textColor = new Color(0.7f, 0.9f, 1f) }
                    };
                return _linkStyle;
            }
        }

        GUIStyle RichTextStyle  => _richTextStyle  ?? (_richTextStyle  = LineStyle(rich: true));
        GUIStyle PlainTextStyle => _plainTextStyle ?? (_plainTextStyle = LineStyle(rich: false));

        static GUIStyle LineStyle(bool rich)
        {
            var src   = EditorStyles.textArea;
            var style = new GUIStyle
            {
                font      = src.font,
                fontSize  = src.fontSize,
                alignment = TextAnchor.UpperLeft,
                clipping  = TextClipping.Overflow,
                richText  = rich,
                wordWrap  = false,
            };
            style.normal.textColor = src.normal.textColor;
            return style;
        }

        public static void Open(string content, string stackTrace = null)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<JsonExpandWindow>();
                _instance.minSize = new Vector2(400, 300);
            }
            var isJson = FindJsonStart(content) >= 0;
            var lines  = Format(content).Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = lines[i].TrimEnd('\r');

            _instance._raw          = content;
            _instance._lines        = lines;
            _instance._coloredLines = isJson ? ColorizeLines(lines) : null;
            _instance._stackTrace   = stackTrace;
            _instance._textWidth    = -1f;
            _instance._anchor       = _instance._caret = new TextPos();
            _instance._dragging     = false;
            _instance._linkStyle      = null;
            _instance._richTextStyle  = null;
            _instance._plainTextStyle = null;
            _instance.titleContent = new GUIContent(content.Length > 40 ? content.Substring(0, 40) + "…" : content);
            _instance.Show();
            _instance.Repaint();
        }

        void OnDestroy() => _instance = null;

        // Auto-scroll while drag-selecting past the top/bottom edge
        void Update()
        {
            var now = EditorApplication.timeSinceStartup;
            var dt  = Mathf.Min(0.1f, (float)(now - _lastUpdate));
            _lastUpdate = now;
            if (!_dragging) return;

            float over = _dragViewPos.y < 0f ? _dragViewPos.y : Mathf.Max(0f, _dragViewPos.y - _viewHeight);
            if (over == 0f) return;
            _scroll.y = Mathf.Max(0f, _scroll.y + Mathf.Clamp(over * 10f, -3000f, 3000f) * dt);
            Repaint();
        }

        void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                return;
            }

            if (_lines == null) _lines = (_raw ?? string.Empty).Split('\n');

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"{_raw?.Length ?? 0} chars", GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    EditorGUIUtility.systemCopyBuffer = _raw;
            }

            _viewHeight = position.height - EditorStyles.toolbar.fixedHeight;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            var hasTrace = !string.IsNullOrEmpty(_stackTrace);
            DrawText(fill: _coloredLines == null && !hasTrace);

            if (hasTrace)
            {
                GUILayout.Space(6);
                EditorGUILayout.LabelField("Stack Trace", EditorStyles.boldLabel);
                GUILayout.Space(2);

                foreach (var line in _stackTrace.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var m = Regex.Match(line, @"\(at (.+):(\d+)\)");
                    if (m.Success)
                    {
                        var path    = m.Groups[1].Value;
                        var lineNum = int.Parse(m.Groups[2].Value);
                        var prefix  = line.Substring(0, m.Index).TrimEnd();
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (!string.IsNullOrEmpty(prefix))
                                GUILayout.Label(prefix, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                            if (GUILayout.Button($"(at {path}:{lineNum})", LinkStyle, GUILayout.ExpandWidth(false)))
                                OpenAtLine(path, lineNum);
                        }
                    }
                    else
                    {
                        GUILayout.Label(line, EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawText(bool fill)
        {
            var box   = EditorStyles.textArea;
            var style = PlainTextStyle;
            float lh  = style.lineHeight;

            if (_textWidth < 0f)
            {
                _textWidth = 0f;
                foreach (var line in _lines)
                    _textWidth = Mathf.Max(_textWidth, style.CalcSize(Temp(line)).x);
            }

            var rect = GUILayoutUtility.GetRect(
                _textWidth + box.padding.horizontal + 8f, _lines.Length * lh + box.padding.vertical, box,
                GUILayout.ExpandWidth(true), GUILayout.MinHeight(60f), GUILayout.ExpandHeight(fill));
            var inner = box.padding.Remove(rect);

            int id = GUIUtility.GetControlID(TextControlHash, FocusType.Keyboard, rect);
            HandleInput(id, inner, lh);

            if (Event.current.type != EventType.Repaint) return;

            box.Draw(rect, GUIContent.none, false, false, false, false);
            EditorGUIUtility.AddCursorRect(inner, MouseCursor.Text);

            // Only lines inside the visible scroll area are drawn
            int first = Mathf.Max(0, Mathf.FloorToInt((_scroll.y - inner.y) / lh) - 1);
            int last  = Mathf.Min(_lines.Length - 1, Mathf.CeilToInt((_scroll.y + _viewHeight - inner.y) / lh) + 1);

            var (a, b) = Ordered();
            bool hasSel = a < b;
            var selColor = GUI.skin.settings.selectionColor;

            for (int i = first; i <= last; i++)
            {
                var lineRect = new Rect(inner.x, inner.y + i * lh, Mathf.Max(inner.width, _textWidth), lh);

                if (hasSel && i >= a.Line && i <= b.Line)
                {
                    float x0 = i == a.Line ? ColX(i, a.Col, lineRect) : lineRect.x;
                    float x1 = i == b.Line ? ColX(i, b.Col, lineRect) : lineRect.xMax;
                    if (x1 > x0) EditorGUI.DrawRect(new Rect(x0, lineRect.y, x1 - x0, lh), selColor);
                }

                var colored = _coloredLines?[i];
                (colored != null ? RichTextStyle : style).Draw(lineRect, Temp(colored ?? _lines[i]), false, false, false, false);
            }
        }

        void HandleInput(int id, Rect inner, float lh)
        {
            var e = Event.current;

            if (_dragging && GUIUtility.hotControl != id) _dragging = false;
            // Rects are dummies during Layout; otherwise re-hit-test so auto-scroll extends the selection
            if (_dragging && e.type != EventType.Layout) _caret = HitTest(_dragViewPos + _scroll, inner, lh);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !inner.Contains(e.mousePosition)) break;
                    GUIUtility.hotControl = GUIUtility.keyboardControl = id;
                    var pos = HitTest(e.mousePosition, inner, lh);
                    if (e.clickCount == 2)      SelectWord(pos);
                    else if (e.clickCount >= 3) SelectLine(pos.Line);
                    else
                    {
                        if (!e.shift) _anchor = pos;
                        _caret = pos;
                        _dragging = true;
                        _dragViewPos = e.mousePosition - _scroll;
                    }
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (!_dragging) break;
                    _dragViewPos = e.mousePosition - _scroll;
                    _caret = HitTest(e.mousePosition, inner, lh);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    _dragging = false;
                    e.Use();
                    break;

                case EventType.ContextClick:
                    if (!inner.Contains(e.mousePosition)) break;
                    var menu = new GenericMenu();
                    if (HasSelection) menu.AddItem(new GUIContent("Copy"), false, CopySelection);
                    else              menu.AddDisabledItem(new GUIContent("Copy"));
                    menu.AddItem(new GUIContent("Select All"), false, SelectAll);
                    menu.ShowAsContext();
                    e.Use();
                    break;

                case EventType.ValidateCommand:
                    if ((e.commandName == "Copy" && HasSelection) || e.commandName == "SelectAll") e.Use();
                    break;

                case EventType.ExecuteCommand:
                    if (e.commandName == "Copy" && HasSelection) { CopySelection(); e.Use(); }
                    else if (e.commandName == "SelectAll")      { SelectAll(); e.Use(); }
                    break;
            }
        }

        bool HasSelection => _anchor < _caret || _caret < _anchor;

        (TextPos, TextPos) Ordered() => _caret < _anchor ? (_caret, _anchor) : (_anchor, _caret);

        TextPos HitTest(Vector2 p, Rect inner, float lh)
        {
            if (p.y < inner.y) return new TextPos(0, 0);
            int line = Mathf.FloorToInt((p.y - inner.y) / lh);
            if (line >= _lines.Length) return new TextPos(_lines.Length - 1, _lines[_lines.Length - 1].Length);

            var lineRect = new Rect(inner.x, inner.y + line * lh, Mathf.Max(inner.width, _textWidth) + 1000f, lh);
            int col = PlainTextStyle.GetCursorStringIndex(lineRect, Temp(_lines[line]), new Vector2(p.x, lineRect.center.y));
            return new TextPos(line, Mathf.Clamp(col, 0, _lines[line].Length));
        }

        float ColX(int line, int col, Rect lineRect) =>
            PlainTextStyle.GetCursorPixelPosition(lineRect, Temp(_lines[line]), col).x;

        void SelectWord(TextPos pos)
        {
            var s = _lines[pos.Line];
            int start = pos.Col, end = pos.Col;
            while (start > 0 && IsWordChar(s[start - 1])) start--;
            while (end < s.Length && IsWordChar(s[end])) end++;
            if (start == end && end < s.Length) end++;
            _anchor = new TextPos(pos.Line, start);
            _caret  = new TextPos(pos.Line, end);
        }

        void SelectLine(int line)
        {
            _anchor = new TextPos(line, 0);
            _caret  = line + 1 < _lines.Length ? new TextPos(line + 1, 0) : new TextPos(line, _lines[line].Length);
        }

        void SelectAll()
        {
            _anchor = new TextPos(0, 0);
            _caret  = new TextPos(_lines.Length - 1, _lines[_lines.Length - 1].Length);
            Repaint();
        }

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        void CopySelection() => EditorGUIUtility.systemCopyBuffer = SelectedText();

        string SelectedText()
        {
            var (a, b) = Ordered();
            if (a.Line == b.Line) return _lines[a.Line].Substring(a.Col, b.Col - a.Col);

            var sb = new StringBuilder(_lines[a.Line].Substring(a.Col));
            for (int i = a.Line + 1; i < b.Line; i++) sb.Append('\n').Append(_lines[i]);
            return sb.Append('\n').Append(_lines[b.Line], 0, b.Col).ToString();
        }

        GUIContent Temp(string text)
        {
            _tmp.text = text;
            return _tmp;
        }

        static void OpenAtLine(string path, int lineNum)
        {
            if (path.StartsWith("Assets/") || path.StartsWith("Packages/"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null) { AssetDatabase.OpenAsset(asset, lineNum); return; }
            }
            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, lineNum, 0);
        }

        // VS2019 dark theme: keys=light blue, strings=orange, numbers=green, keywords=blue
        static readonly Regex ColorizeRx = new Regex(
            @"(""(?:[^""\\]|\\.)*"")(\s*:)|(""(?:[^""\\]|\\.)*"")|(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)|(\b(?:true|false|null)\b)",
            RegexOptions.Compiled);

        // Lines containing < or > stay plain: glyphs must match the plain text for hit-testing
        static string[] ColorizeLines(string[] lines)
        {
            var result = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].IndexOf('<') < 0 && lines[i].IndexOf('>') < 0)
                    result[i] = Colorize(lines[i]);
            return result;
        }

        static string Colorize(string prettyJson)
        {
            return ColorizeRx.Replace(prettyJson, m =>
            {
                if (m.Groups[2].Success) return $"<color=#9CDCFE>{m.Groups[1].Value}</color>{m.Groups[2].Value}";
                if (m.Groups[3].Success) return $"<color=#CE9178>{m.Groups[3].Value}</color>";
                if (m.Groups[4].Success) return $"<color=#B5CEA8>{m.Groups[4].Value}</color>";
                if (m.Groups[5].Success) return $"<color=#569CD6>{m.Groups[5].Value}</color>";
                return m.Value;
            });
        }

        // Finds first real JSON start ({" or [{ or [" etc), returns -1 if none
        static int FindJsonStart(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '{' && c != '[') continue;

                int next = i + 1;
                while (next < s.Length && (s[next] == ' ' || s[next] == '\t' || s[next] == '\r' || s[next] == '\n')) next++;
                if (next >= s.Length) continue;

                char after = s[next];
                bool valid = c == '{'
                    ? after == '"' || after == '}'
                    : after == '{' || after == '[' || after == ']' || after == '"'
                      || char.IsDigit(after) || after == 't' || after == 'f' || after == 'n' || after == '-';

                if (valid) return i;
            }
            return -1;
        }

        static string Format(string content)
        {
            int jsonStart = FindJsonStart(content);
            if (jsonStart < 0) return content;  // no JSON — mostrar tal cual

            string prefix = content.Substring(0, jsonStart).TrimEnd();
            string json = content.Substring(jsonStart);
            string pretty = PrettyPrint(json);

            return string.IsNullOrEmpty(prefix) ? pretty : prefix + "\n\n" + pretty;
        }

        static string PrettyPrint(string json)
        {
            try
            {
                int indent = 0;
                bool inString = false;
                var sb = new StringBuilder(json.Length * 2);

                for (int i = 0; i < json.Length; i++)
                {
                    char c = json[i];

                    if (c == '"' && (i == 0 || json[i - 1] != '\\'))
                        inString = !inString;

                    if (inString)
                    {
                        sb.Append(c);
                        continue;
                    }

                    switch (c)
                    {
                        case '{':
                        case '[':
                            sb.Append(c);
                            sb.AppendLine();
                            sb.Append(new string(' ', ++indent * 2));
                            break;
                        case '}':
                        case ']':
                            sb.AppendLine();
                            sb.Append(new string(' ', --indent * 2));
                            sb.Append(c);
                            break;
                        case ',':
                            sb.Append(c);
                            sb.AppendLine();
                            sb.Append(new string(' ', indent * 2));
                            break;
                        case ':':
                            sb.Append(": ");
                            break;
                        case ' ':
                        case '\t':
                        case '\n':
                        case '\r':
                            break;
                        default:
                            sb.Append(c);
                            break;
                    }
                }

                return sb.ToString();
            }
            catch
            {
                return json;
            }
        }
    }
}
