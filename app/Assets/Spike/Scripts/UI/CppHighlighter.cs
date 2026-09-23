using System.Collections.Generic;
using System.Text;

namespace CoreEngine.Spike.UI
{
    /// <summary>
    /// Small C++/Arduino tokenizer for the code editor spike (docs/04 §code editor). Colours one line at a
    /// time and carries the block-comment state from line to line, so an edit only re-colours the lines
    /// whose state changed. Output is UI Toolkit rich text; code text sits inside noparse tags so
    /// characters such as &lt; are never read as tags.
    /// </summary>
    public static class CppHighlighter
    {
        const string KeywordColor = "#569CD6";
        const string ControlColor = "#C586C0";
        const string TypeColor = "#4EC9B0";
        const string ArduinoColor = "#4FC1FF";
        const string FunctionColor = "#DCDCAA";
        const string StringColor = "#CE9178";
        const string NumberColor = "#B5CEA8";
        const string CommentColor = "#6A9955";
        const string PreprocessorColor = "#C586C0";

        static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "const", "static", "volatile", "unsigned", "signed", "struct", "class", "enum", "typedef", "sizeof",
            "new", "delete", "public", "private", "protected", "virtual", "inline", "extern", "true", "false",
            "nullptr", "this", "template", "typename", "namespace", "using", "operator", "PROGMEM",
        };

        static readonly HashSet<string> ControlWords = new HashSet<string>
        {
            "if", "else", "for", "while", "do", "switch", "case", "default", "break", "continue", "return", "goto",
        };

        static readonly HashSet<string> Types = new HashSet<string>
        {
            "void", "bool", "boolean", "char", "byte", "int", "long", "short", "float", "double", "word", "String",
            "uint8_t", "int8_t", "uint16_t", "int16_t", "uint32_t", "int32_t", "uint64_t", "int64_t", "size_t", "auto",
        };

        static readonly HashSet<string> ArduinoNames = new HashSet<string>
        {
            "HIGH", "LOW", "INPUT", "OUTPUT", "INPUT_PULLUP", "LED_BUILTIN", "Serial", "A0", "A1", "A2", "A3", "A4", "A5",
            "setup", "loop", "pinMode", "digitalWrite", "digitalRead", "analogRead", "analogWrite", "delay",
            "delayMicroseconds", "millis", "micros", "pulseIn", "tone", "noTone", "attachInterrupt", "detachInterrupt",
            "map", "constrain", "min", "max", "abs", "random", "randomSeed",
        };

        /// <param name="inComment">Block-comment state before the line; updated to the state after it.</param>
        public static string Highlight(string line, ref bool inComment, StringBuilder sb)
        {
            sb.Clear();
            int i = 0;
            int n = line.Length;

            if (!inComment)
            {
                int first = 0;
                while (first < n && line[first] == ' ') first++;
                if (first < n && line[first] == '#')
                {
                    Append(sb, null, line, 0, first);
                    // A preprocessor line may still end in a comment.
                    int comment = line.IndexOf("//", first, System.StringComparison.Ordinal);
                    int end = comment >= 0 ? comment : n;
                    Append(sb, PreprocessorColor, line, first, end);
                    if (comment >= 0) Append(sb, CommentColor, line, comment, n);
                    return sb.ToString();
                }
            }

            while (i < n)
            {
                if (inComment)
                {
                    int close = line.IndexOf("*/", i, System.StringComparison.Ordinal);
                    int end = close >= 0 ? close + 2 : n;
                    Append(sb, CommentColor, line, i, end);
                    i = end;
                    if (close >= 0) inComment = false;
                    continue;
                }

                char c = line[i];
                if (c == '/' && i + 1 < n && line[i + 1] == '/')
                {
                    Append(sb, CommentColor, line, i, n);
                    break;
                }
                if (c == '/' && i + 1 < n && line[i + 1] == '*')
                {
                    inComment = true;
                    int close = line.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                    int end = close >= 0 ? close + 2 : n;
                    Append(sb, CommentColor, line, i, end);
                    i = end;
                    if (close >= 0) inComment = false;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    int end = i + 1;
                    while (end < n && line[end] != c)
                    {
                        if (line[end] == '\\') end++;
                        end++;
                    }
                    end = System.Math.Min(end + 1, n);
                    Append(sb, StringColor, line, i, end);
                    i = end;
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(line[i + 1])))
                {
                    int end = i + 1;
                    while (end < n && (char.IsLetterOrDigit(line[end]) || line[end] == '.')) end++; // 0x1F, 3.3f, 1000UL
                    Append(sb, NumberColor, line, i, end);
                    i = end;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int end = i + 1;
                    while (end < n && (char.IsLetterOrDigit(line[end]) || line[end] == '_')) end++;
                    string word = line.Substring(i, end - i);
                    int next = end;
                    while (next < n && line[next] == ' ') next++;
                    string? color =
                        ControlWords.Contains(word) ? ControlColor :
                        Keywords.Contains(word) ? KeywordColor :
                        Types.Contains(word) ? TypeColor :
                        ArduinoNames.Contains(word) ? ArduinoColor :
                        next < n && line[next] == '(' ? FunctionColor : null;
                    Append(sb, color, line, i, end);
                    i = end;
                    continue;
                }

                int run = i + 1; // punctuation, operators and spaces up to the next token
                while (run < n && !IsTokenStart(line, run)) run++;
                Append(sb, null, line, i, run);
                i = run;
            }
            return sb.ToString();
        }

        static bool IsTokenStart(string line, int i)
        {
            char c = line[i];
            return char.IsLetterOrDigit(c) || c == '_' || c == '"' || c == '\'' ||
                   (c == '/' && i + 1 < line.Length && (line[i + 1] == '/' || line[i + 1] == '*'));
        }

        static void Append(StringBuilder sb, string? color, string line, int start, int end)
        {
            if (end <= start) return;
            if (color != null) sb.Append("<color=").Append(color).Append('>');
            sb.Append("<noparse>").Append(line, start, end - start).Append("</noparse>");
            if (color != null) sb.Append("</color>");
        }
    }
}
