using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sodium.Tools
{
    public class JsonExpandWindow : EditorWindow
    {
        string _raw;
        string _pretty;
        Vector2 _scroll;

        static JsonExpandWindow _instance;

        public static void Open(string content)
        {
            if (_instance == null)
            {
                _instance = CreateInstance<JsonExpandWindow>();
                _instance.minSize = new Vector2(400, 300);
            }
            _instance._raw = content;
            _instance._pretty = Format(content);
            _instance.titleContent = new GUIContent(content.Length > 40 ? content.Substring(0, 40) + "…" : content);
            _instance.ShowUtility();
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
            EditorGUILayout.TextArea(_pretty ?? _raw ?? string.Empty, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // Finds first real JSON start ({" or [{ or [" etc), returns -1 if none
        static int FindJsonStart(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '{' && c != '[') continue;

                int next = i + 1;
                while (next < s.Length && (s[next] == ' ' || s[next] == '\t' || s[next] == '\n')) next++;
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
