using System.Text;

namespace OotMM.Core;

/// <summary>SimpleJson's exact string escaping, shared by every hand serializer
/// that must stay byte-identical with the C++ writers.</summary>
internal static class JsonText
{
    internal static string EscapeString(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default: text.Append(c); break;
            }
        }

        return text.Append('"').ToString();
    }
}
