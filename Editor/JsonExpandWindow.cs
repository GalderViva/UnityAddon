using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    public class JsonExpandWindow : EditorWindow
    {
        // IMGUI can't position the cursor/selection highlight past ~16384 chars of a single
        // control's string (rich-text tags included), so long texts are drawn as several labels.
        const int MaxChunkChars = 15000;

        string _raw;
        string _pretty;
        string[] _chunks;
        bool _chunksRich;
        string _stackTrace;
        Vector2 _scroll;
        GUIStyle _linkStyle;
        GUIStyle _richTextStyle;
        GUIStyle _plainTextStyle;

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

        GUIStyle RichTextStyle  => _richTextStyle  ?? (_richTextStyle  = ChunkStyle(rich: true));
        GUIStyle PlainTextStyle => _plainTextStyle ?? (_plainTextStyle = ChunkStyle(rich: false));

        // Chunks sit inside one textArea-styled group, so they carry no background, margin or padding
        static GUIStyle ChunkStyle(bool rich)
        {
            var src   = EditorStyles.textArea;
            var style = new GUIStyle
            {
                font      = src.font,
                fontSize  = src.fontSize,
                alignment = TextAnchor.UpperLeft,
                clipping  = src.clipping,
                richText  = rich,
                wordWrap  = !rich,
            };
            style.normal.textColor    = src.normal.textColor;
            style.hover.textColor     = src.hover.textColor;
            style.active.textColor    = src.active.textColor;
            style.focused.textColor   = src.focused.textColor;
            style.onNormal.textColor  = src.onNormal.textColor;
            style.onHover.textColor   = src.onHover.textColor;
            style.onActive.textColor  = src.onActive.textColor;
            style.onFocused.textColor = src.onFocused.textColor;
            return style;
        }

        public static void Open(string content, string stackTrace = null)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<JsonExpandWindow>();
                _instance.minSize = new Vector2(400, 300);
            }
            _instance._raw            = content;
            _instance._pretty         = Format(content);
            _instance._chunksRich     = FindJsonStart(content) >= 0;
            _instance._chunks         = BuildChunks(_instance._pretty, _instance._chunksRich);
            _instance._stackTrace     = stackTrace;
            _instance._linkStyle      = null;
            _instance._richTextStyle  = null;
            _instance._plainTextStyle = null;
            _instance.titleContent = new GUIContent(content.Length > 40 ? content.Substring(0, 40) + "…" : content);
            _instance.Show();
            _instance.Repaint();
        }

        void OnDestroy() => _instance = null;

        void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"{_raw?.Length ?? 0} chars", GUILayout.Width(80));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(50)))
                    EditorGUIUtility.systemCopyBuffer = _raw;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            var hasTrace = !string.IsNullOrEmpty(_stackTrace);
            var style    = _chunksRich ? RichTextStyle : PlainTextStyle;
            var fill     = !_chunksRich && !hasTrace;

            using (new EditorGUILayout.VerticalScope(EditorStyles.textArea, GUILayout.MinHeight(60f), GUILayout.ExpandHeight(fill)))
            {
                foreach (var chunk in _chunks ?? new[] { _raw ?? string.Empty })
                {
                    var content = new GUIContent(chunk);
                    var rect    = style.wordWrap
                        ? GUILayoutUtility.GetRect(content, style, GUILayout.ExpandWidth(true))
                        : GUILayoutUtility.GetRect(0f, style.CalcHeight(content, 0f), style, GUILayout.ExpandWidth(true));
                    EditorGUI.SelectableLabel(rect, chunk, style);
                }
            }

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

        // Splits text on line boundaries into pieces of at most MaxChunkChars (after colorizing).
        // Colorizing per line keeps every <color> tag inside its own piece.
        static string[] BuildChunks(string text, bool colorize)
        {
            var chunks = new List<string>();
            var sb     = new StringBuilder();
            int lines  = 0;

            foreach (var line in text.Split('\n'))
            {
                foreach (var piece in LinePieces(line, colorize))
                {
                    if (lines > 0 && sb.Length + 1 + piece.Length > MaxChunkChars)
                    {
                        chunks.Add(sb.ToString());
                        sb.Clear();
                        lines = 0;
                    }
                    if (lines > 0) sb.Append('\n');
                    sb.Append(piece);
                    lines++;
                }
            }
            chunks.Add(sb.ToString());
            return chunks.ToArray();
        }

        // A single line too long for one chunk is hard-split and left uncolored
        static IEnumerable<string> LinePieces(string line, bool colorize)
        {
            var text = colorize ? Colorize(line) : line;
            if (text.Length <= MaxChunkChars)
            {
                yield return text;
                yield break;
            }
            if (colorize) line = Escape(line);
            for (int i = 0; i < line.Length; i += MaxChunkChars)
                yield return line.Substring(i, Mathf.Min(MaxChunkChars, line.Length - i));
        }

        static string Escape(string s) => s.Replace("<", "&lt;").Replace(">", "&gt;");

        static string Colorize(string prettyJson)
        {
            prettyJson = Escape(prettyJson);
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
