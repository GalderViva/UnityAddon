using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    public class JsonExpandWindow : EditorWindow
    {
        string _raw;
        string _pretty;
        string _prettyColored;
        string _stackTrace;
        Vector2 _scroll;
        GUIStyle _linkStyle;
        GUIStyle _richTextStyle;
        GUIStyle _invisibleStyle;

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

        GUIStyle RichTextStyle
        {
            get
            {
                if (_richTextStyle == null)
                    _richTextStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        richText = true,
                        wordWrap = false,
                    };
                return _richTextStyle;
            }
        }

        public static void Open(string content, string stackTrace = null)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<JsonExpandWindow>();
                _instance.minSize = new Vector2(400, 300);
            }
            _instance._raw           = content;
            _instance._pretty        = Format(content);
            _instance._prettyColored = FindJsonStart(content) >= 0 ? Colorize(_instance._pretty) : null;
            _instance._stackTrace    = stackTrace;
            _instance._linkStyle      = null;
            _instance._richTextStyle  = null;
            _instance._invisibleStyle = null;
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

            var hasTrace  = !string.IsNullOrEmpty(_stackTrace);
            var plainText = _pretty ?? _raw ?? string.Empty;

            if (_prettyColored != null)
            {
                float h = Mathf.Max(60f, EditorStyles.textArea.CalcHeight(new GUIContent(plainText), position.width - 24f));
                var rect = EditorGUILayout.GetControlRect(false, h);
                // Bottom layer: colored text for display
                GUI.Label(rect, _prettyColored, RichTextStyle);
                // Top layer: invisible plain text for selection — copies are tag-free
                if (_invisibleStyle == null)
                {
                    _invisibleStyle = new GUIStyle(RichTextStyle) { richText = false };
                    _invisibleStyle.normal.textColor   = Color.clear;
                    _invisibleStyle.focused.textColor  = Color.clear;
                    _invisibleStyle.active.textColor   = Color.clear;
                    _invisibleStyle.hover.textColor    = Color.clear;
                    _invisibleStyle.normal.background  = null;
                    _invisibleStyle.focused.background = null;
                    _invisibleStyle.active.background  = null;
                    _invisibleStyle.hover.background   = null;
                }
                EditorGUI.SelectableLabel(rect, plainText, _invisibleStyle);
            }
            else if (hasTrace)
            {
                float h = Mathf.Max(60f, EditorStyles.textArea.CalcHeight(new GUIContent(plainText), position.width - 24f));
                EditorGUILayout.TextArea(plainText, GUILayout.Height(h));
            }
            else
            {
                EditorGUILayout.TextArea(plainText, GUILayout.ExpandHeight(true));
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

        static string Colorize(string prettyJson)
        {
            prettyJson = prettyJson.Replace("<", "&lt;").Replace(">", "&gt;");
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
