using System.IO;
using System.Text;

namespace MarkEditWin.Services;

public enum LineEnding
{
    LF,
    CRLF,
    CR,
}

/// <summary>
/// One open document: file identity, encoding, line endings and dirty tracking.
/// The text itself always lives in the web editor, this type only stores the metadata
/// that the native host needs to read and write files.
/// </summary>
public sealed class DocumentSession
{
    public string? FilePath { get; set; }

    public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public LineEnding LineEnding { get; set; } = LineEnding.CRLF;

    public bool IsDirty { get; set; }

    public bool HasBeenEdited { get; set; }

    /// <summary>True once the editor reported that the document was loaded into the page.</summary>
    public bool IsEditorReady { get; set; }

    /// <summary>Set while the host itself is pushing a document, so notifications are ignored.</summary>
    public bool IsResetting { get; set; }

    /// <summary>Selection restored when the document finishes loading.</summary>
    public (int Anchor, int Head)? PendingSelection { get; set; }

    public string DisplayName => FilePath is null ? "Untitled" : Path.GetFileName(FilePath);

    public string? DirectoryPath => FilePath is null ? null : Path.GetDirectoryName(FilePath);

    public static DocumentSession FromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, offset) = DetectEncoding(bytes);
        var text = DecodeText(bytes, encoding, offset);

        return new DocumentSession
        {
            FilePath = path,
            Encoding = encoding,
            LineEnding = DetectLineEnding(text),
        };
    }

    public string ReadText()
    {
        if (FilePath is null)
        {
            return string.Empty;
        }

        var bytes = File.ReadAllBytes(FilePath);
        var (encoding, offset) = DetectEncoding(bytes);
        return DecodeText(bytes, encoding, offset);
    }

    /// <summary>Points this session at a file and refreshes encoding and line ending metadata.</summary>
    public void LoadFrom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (encoding, offset) = DetectEncoding(bytes);

        FilePath = path;
        Encoding = encoding;
        LineEnding = DetectLineEnding(DecodeText(bytes, encoding, offset));
        IsDirty = false;
        HasBeenEdited = false;
    }

    /// <summary>Normalizes line endings to the platform convention for the document.</summary>
    public string NormalizeForSave(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return LineEnding switch
        {
            LineEnding.CRLF => normalized.Replace("\n", "\r\n"),
            LineEnding.CR => normalized.Replace("\n", "\r"),
            _ => normalized,
        };
    }

    public void WriteText(string text)
    {
        if (FilePath is null)
        {
            return;
        }

        File.WriteAllBytes(FilePath, Encoding.GetBytes(NormalizeForSave(text)));
        IsDirty = false;
    }

    /// <summary>Decodes bytes with BOM detection and a legacy code page fallback.</summary>
    public static string Decode(byte[] bytes)
    {
        var (encoding, offset) = DetectEncoding(bytes);
        return DecodeText(bytes, encoding, offset);
    }

    private static (Encoding Encoding, int Offset) DetectEncoding(byte[] bytes)    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(true), 3);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return (new UTF32Encoding(false, true), 4);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return (new UTF32Encoding(true, true), 4);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (new UnicodeEncoding(false, true), 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (new UnicodeEncoding(true, true), 2);
        }

        return (new UTF8Encoding(false), 0);
    }

    private static string DecodeText(byte[] bytes, Encoding encoding, int offset)
    {
        try
        {
            return encoding.GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8/UTF-16, fall back to the legacy code page of the current user.
            var ansi = Encoding.GetEncoding(0, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            return ansi.GetString(bytes, offset, bytes.Length - offset);
        }
    }

    public static LineEnding DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf >= lf && crlf >= cr && crlf > 0)
        {
            return LineEnding.CRLF;
        }

        if (cr > lf && cr > 0)
        {
            return LineEnding.CR;
        }

        return LineEnding.LF;
    }
}
