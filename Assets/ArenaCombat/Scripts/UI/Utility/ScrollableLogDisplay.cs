using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArenaCombat.UI.Utility
{
    [Serializable]
    public class ScrollableLogDisplay
    {
        [SerializeField] private TMP_Text logText;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private int maxLines = 50;
        [SerializeField] private string timestampFormat = "HH:mm:ss";

        public bool IsValid => logText != null;

        public void Append(string message, string unityLogPrefix = null)
        {
            string timestamp = DateTime.Now.ToString(timestampFormat);
            string entry = $"[{timestamp}] {message}\n";

            if (unityLogPrefix != null)
                Debug.Log($"[{unityLogPrefix}] {message}");

            AppendRaw(entry);
        }

        public void AppendRich(string richTextEntry, string unityLogPrefix = null, LogType logType = LogType.Log)
        {
            string timestamp = DateTime.Now.ToString(timestampFormat);
            string entry = $"[{timestamp}] {richTextEntry}\n";

            if (unityLogPrefix != null)
            {
                string stripped = System.Text.RegularExpressions.Regex.Replace(richTextEntry, "<.*?>", "");
                switch (logType)
                {
                    case LogType.Error:
                        Debug.LogError($"[{unityLogPrefix}] {stripped}");
                        break;
                    case LogType.Warning:
                        Debug.LogWarning($"[{unityLogPrefix}] {stripped}");
                        break;
                    default:
                        Debug.Log($"[{unityLogPrefix}] {stripped}");
                        break;
                }
            }

            AppendRaw(entry);
        }

        public void Clear()
        {
            if (logText != null)
                logText.text = "";
        }

        private void AppendRaw(string entry)
        {
            if (logText == null) return;

            logText.text += entry;

            string[] lines = logText.text.Split('\n');
            if (lines.Length > maxLines)
            {
                logText.text = string.Join("\n", lines, lines.Length - maxLines, maxLines);
            }

            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }
}
