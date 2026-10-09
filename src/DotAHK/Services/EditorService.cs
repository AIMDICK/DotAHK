using System.Text;

namespace DotAHK.Services;

/// <summary>
/// Default <see cref="IEditorService"/>. Reads and writes script text while
/// preserving the source file's byte-order mark: UTF-16 files stay UTF-16, and
/// files without a BOM are written as UTF-8 with a BOM (AutoHotkey v2's default).
/// </summary>
public sealed class EditorService : IEditorService
{
    public async Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var encoding = DetectEncoding(filePath);

        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        await using var writer = new StreamWriter(stream, encoding);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the encoding used by the existing file so the BOM round-trips,
    /// defaulting to UTF-8 with a BOM (AutoHotkey v2's convention) for new files.
    /// </summary>
    private static Encoding DetectEncoding(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                Span<byte> head = stackalloc byte[3];
                using var stream = new FileStream(
                    filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var read = stream.Read(head);

                if (read >= 2 && head[0] == 0xFF && head[1] == 0xFE)
                {
                    return Encoding.Unicode;
                }

                if (read >= 2 && head[0] == 0xFE && head[1] == 0xFF)
                {
                    return Encoding.BigEndianUnicode;
                }

                if (read >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                {
                    return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                }
            }
        }
        catch (Exception)
        {
            // Fall through to the default encoding below.
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    }
}
