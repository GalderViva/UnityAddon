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

        public static void Open(string json)
        {
            var win = CreateInstance<JsonExpandWindow>();
            var title = json.Length > 40 ? json.Substring(0, 40) + "…" : json;
            win.titleContent = new GUIContent(title);
            win._raw = json;
            win._pretty = PrettyPrint(json);
            win.minSize = new Vector2(400, 300);
            win.ShowUtility();
        }

        void OnGUI()
        {
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
