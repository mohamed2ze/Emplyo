using System.Text;

namespace Emplyo.Web.Services;

public static class Csv
{
    /// <summary>
    /// Escapes a value for CSV output and neutralises spreadsheet formula injection.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var v = value;
        if (v.Length > 0 && v[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
        {
            v = "'" + v;
        }

        if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
        {
            v = "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        return v;
    }

    public static string Line(params string?[] values)
        => string.Join(",", values.Select(Escape));

    public static byte[] ToBytes(IEnumerable<string> lines)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(string.Join("\r\n", lines) + "\r\n");
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }
}
