using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;

namespace UU9.Muck.Translater
{
    public static class TranslationManager
    {
        private static readonly Dictionary<string, string> Translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ReverseTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex RichTextRegex = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static string _translationDir;
        private static ManualLogSource _logger;
        private static List<string> _cachedSortedKeys;
        private static readonly List<KeyValuePair<Regex, string>> Templates = new List<KeyValuePair<Regex, string>>();
        private static readonly Dictionary<string, string> ObservedOriginals = new Dictionary<string, string>();
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}");

        public static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
            _translationDir = Path.Combine(Paths.ConfigPath, "UU9.Muck.Translater", "Translation");
            Directory.CreateDirectory(_translationDir);
            LoadTranslations();
        }

        public static void LoadTranslations()
        {
            Translations.Clear();
            Templates.Clear();
            ObservedOriginals.Clear();
            ReverseTranslations.Clear();
            _cachedSortedKeys = null;

            if (string.IsNullOrEmpty(_translationDir) || !Directory.Exists(_translationDir)) return;

            try
            {
                string[] cfgFiles = Directory.GetFiles(_translationDir, "*.cfg", SearchOption.AllDirectories);
                Array.Sort(cfgFiles, StringComparer.OrdinalIgnoreCase);
                int totalEntries = 0;
                var allTranslatedCharacters = new StringBuilder();

                foreach (string filePath in cfgFiles)
                {
                    foreach (string line in File.ReadAllLines(filePath, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//")) continue;

                        int equalsIndex = line.IndexOf('=');
                        if (equalsIndex <= 0) continue;

                        string key = line.Substring(0, equalsIndex).Trim().Replace("\\n", "\n").Replace("\\r", "\r");
                        string value = line.Substring(equalsIndex + 1).Trim().Replace("\\n", "\n").Replace("\\r", "\r");
                        if (string.IsNullOrEmpty(key) || Translations.ContainsKey(key)) continue;

                        Translations[key] = value;
                        if (!string.IsNullOrEmpty(value) && !ReverseTranslations.ContainsKey(value)) ReverseTranslations[value] = key;
                        allTranslatedCharacters.Append(value);
                        totalEntries++;
                    }
                }

                foreach (string key in GetSortedTranslationKeys())
                {
                    if (!Placeholder.IsMatch(key)) continue;
                    var pattern = new StringBuilder(@"\A");
                    int position = 0;
                    foreach (Match placeholder in Placeholder.Matches(key))
                    {
                        pattern.Append(Regex.Escape(key.Substring(position, placeholder.Index - position)));
                        pattern.Append("(?<arg" + placeholder.Groups[1].Value + ">.+?)");
                        position = placeholder.Index + placeholder.Length;
                    }
                    pattern.Append(Regex.Escape(key.Substring(position))).Append(@"\z");
                    Templates.Add(new KeyValuePair<Regex, string>(new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline), Translations[key]));
                }

                for (char c = ' '; c <= '~'; c++) allTranslatedCharacters.Append(c);
                FontManager.AddCharactersToFont(allTranslatedCharacters.ToString());
                _logger?.LogInfo($"[UU9 Translater] 从 {cfgFiles.Length} 个词典文件加载 {totalEntries} 条翻译文本：{_translationDir}");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[UU9 Translater] 读取词典目录失败: {ex.Message}");
            }
        }

        public static bool TryGetOriginal(string translated, out string original)
        {
            original = null;
            if (string.IsNullOrWhiteSpace(translated)) return false;
            if (ObservedOriginals.TryGetValue(translated, out original)) return true;
            if (ReverseTranslations.TryGetValue(translated, out original)) return true;

            string trimmed = translated.Trim();
            if (ReverseTranslations.TryGetValue(trimmed, out string value))
            {
                original = PreserveOuterWhitespace(translated, value);
                return true;
            }

            return TryTranslateLines(translated, ReverseTranslations, out original);
        }

        public static bool TryGetTranslation(string original, out string translated)
        {
            translated = null;
            if (string.IsNullOrWhiteSpace(original)) return false;
            // Do not translate our own output again when multiple TMP hooks run.
            if (ObservedOriginals.ContainsKey(original) || ReverseTranslations.ContainsKey(original)) return false;
            string result = TranslateContent(original);
            if (result == original) return false;
            translated = result;
            if (ObservedOriginals.Count > 4096) ObservedOriginals.Clear();
            ObservedOriginals[result] = original;
            return true;
        }

        private static string TranslateContent(string input)
        {
            string trimmed = input.Trim();
            string exact;
            if (Translations.TryGetValue(trimmed, out exact)) return PreserveOuterWhitespace(input, exact);
            foreach (var template in Templates)
            {
                Match match = template.Key.Match(trimmed);
                if (!match.Success) continue;
                string value = Placeholder.Replace(template.Value, m => {
                    string argument = match.Groups["arg" + m.Groups[1].Value].Value;
                    string localized;
                    return Translations.TryGetValue(argument, out localized) ? localized : argument;
                });
                return PreserveOuterWhitespace(input, value);
            }
            // Preserve color, size, links and other markup verbatim.
            if (RichTextRegex.IsMatch(input))
            {
                string[] parts = Regex.Split(input, @"(<[^>]*>)");
                for (int i = 0; i < parts.Length; i++)
                    if (parts[i].Length > 0 && !RichTextRegex.IsMatch(parts[i])) parts[i] = TranslateContent(parts[i]);
                return string.Concat(parts);
            }
            if (input.Contains("\n"))
            {
                string[] lines = input.Split('\n');
                for (int i = 0; i < lines.Length; i++) lines[i] = TranslateContent(lines[i]);
                return string.Join("\n", lines);
            }
            string result = input;
            foreach (string key in GetSortedTranslationKeys())
            {
                if (Placeholder.IsMatch(key) || result.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                result = ReplaceWordOrPhrase(result, key, Translations[key]);
            }
            return result;
        }

        private static bool TryTranslateLines(string input, Dictionary<string, string> dictionary, out string translated)
        {
            translated = null;
            if (!input.Contains("\n")) return false;

            string[] lines = input.Split('\n');
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim('\r', ' ');
                if (!dictionary.TryGetValue(trimmed, out string replacement))
                {
                    string cleanLine = RichTextRegex.Replace(trimmed, string.Empty);
                    if (!dictionary.TryGetValue(cleanLine, out replacement)) continue;
                    lines[i] = line.Replace(cleanLine, replacement);
                }
                else
                {
                    lines[i] = line.Replace(trimmed, replacement);
                }
                changed = true;
            }

            if (!changed) return false;
            translated = string.Join("\n", lines);
            return true;
        }

        private static string PreserveOuterWhitespace(string original, string replacement)
        {
            int leading = original.Length - original.TrimStart().Length;
            int trailing = original.Length - original.TrimEnd().Length;
            return original.Substring(0, leading) + replacement + original.Substring(original.Length - trailing);
        }

        private static List<string> GetSortedTranslationKeys()
        {
            if (_cachedSortedKeys != null) return _cachedSortedKeys;
            _cachedSortedKeys = new List<string>(Translations.Keys);
            _cachedSortedKeys.Sort((a, b) => b.Length.CompareTo(a.Length));
            return _cachedSortedKeys;
        }

        private static string ReplaceWordOrPhrase(string input, string pattern, string replacement)
        {
            bool isPureWord = true;
            foreach (char c in pattern)
            {
                if (char.IsLetterOrDigit(c) || c == '_') continue;
                isPureWord = false;
                break;
            }

            string regexPattern = Regex.Escape(pattern);
            if (isPureWord) regexPattern = @"\b" + regexPattern + @"\b";
            return Regex.Replace(input, regexPattern, m => replacement, RegexOptions.IgnoreCase);
        }
    }
}
