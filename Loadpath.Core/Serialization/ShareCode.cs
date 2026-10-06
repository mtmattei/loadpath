using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadpath.Core.Editing;
using Loadpath.Core.Model;

namespace Loadpath.Core.Serialization;

/// <summary>
/// A document as one line of text that survives chat, email and issue trackers:
/// <c>LP1.</c> followed by base64url of the deflated compact JSON document.
/// </summary>
public static partial class ShareCode
{
    public const string Prefix = "LP1.";

    public static string Encode(StructureDocument doc)
    {
        var json = Encoding.UTF8.GetBytes(DocumentSerializer.Serialize(doc, indented: false));
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(json);
        return Prefix + Convert.ToBase64String(buffer.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Read a design from pasted text: a share code (possibly inside surrounding text, such as a chat message)
    /// or a raw <c>.loadpath</c> JSON document. Returns false with a user-facing reason when nothing usable is found.
    /// </summary>
    public static bool TryDecode(string? text, out DocumentSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = "";
        var t = text?.Trim() ?? "";
        if (t.Length == 0) { error = "Clipboard has no Loadpath design"; return false; }

        if (t.StartsWith('{'))
        {
            try { snapshot = DocumentSerializer.Deserialize(t); return true; }
            catch (InvalidDataException ex) { error = ex.Message; return false; }
            catch (JsonException) { error = "That document is not valid Loadpath JSON"; return false; }
        }

        var match = CodePattern().Match(t);
        if (!match.Success) { error = "Clipboard has no Loadpath design"; return false; }
        try
        {
            var b64 = match.Groups[1].Value.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var input = new MemoryStream(Convert.FromBase64String(b64));
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            snapshot = DocumentSerializer.Deserialize(reader.ReadToEnd());
            return true;
        }
        catch (InvalidDataException ex) when (ex.Message.StartsWith("Document version", StringComparison.Ordinal) || ex.Message.StartsWith("Member", StringComparison.Ordinal))
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException or IOException)
        {
            error = "That share code is damaged or incomplete";
            return false;
        }
    }

    [GeneratedRegex(@"LP1\.([A-Za-z0-9_\-]+)")]
    private static partial Regex CodePattern();
}
