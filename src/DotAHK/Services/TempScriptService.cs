using System.Text;
using DotAHK.Models;

namespace DotAHK.Services;

/// <summary>
/// Default <see cref="ITempScriptService"/>. Writes an execution copy under
/// <c>%LOCALAPPDATA%\DotAHK\Temp</c> with <c>#NoTrayIcon</c> prepended, preserving
/// the source file's byte-order mark and body bytes exactly so the copy stays valid
/// for both AutoHotkey v1 (often ANSI/UTF-8) and v2 (UTF-8) scripts.
/// </summary>
public sealed class TempScriptService : ITempScriptService
{
    private const string NoTrayIconDirective = "#NoTrayIcon";
    private const string LineTerminator = "\r\n";

    private readonly string _tempDirectory;

    public TempScriptService()
    {
        _tempDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotAHK",
            "Temp");
    }

    public async Task<string?> CreateExecutionCopyAsync(
        AhkScript script,
        bool injectNoTrayIcon,
        CancellationToken cancellationToken = default)
    {
        if (!injectNoTrayIcon || !File.Exists(script.FilePath))
        {
            // Nothing to inject: launch the user's script in place.
            return script.FilePath;
        }

        try
        {
            Directory.CreateDirectory(_tempDirectory);

            var bytes = await File.ReadAllBytesAsync(script.FilePath, cancellationToken)
                .ConfigureAwait(false);

            var output = InjectNoTrayIcon(bytes);

            var tempPath = Path.Combine(_tempDirectory, BuildFileName(script.FilePath));
            await File.WriteAllBytesAsync(tempPath, output, cancellationToken).ConfigureAwait(false);
            return tempPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If the copy cannot be produced, fall back to the original script so
            // execution still works (it simply keeps its own tray icon).
            return script.FilePath;
        }
    }

    /// <summary>
    /// Returns the file bytes with <c>#NoTrayIcon</c> prepended immediately after
    /// any byte-order mark. The work lives in this synchronous helper because
    /// ref structs such as <see cref="Span{T}"/> cannot be used inside an async
    /// method.
    /// </summary>
    private static byte[] InjectNoTrayIcon(byte[] bytes)
    {
        var (encoding, preambleLength) = DetectEncoding(bytes);
        var body = bytes.AsSpan(preambleLength);

        if (ContainsNoTrayIcon(body, encoding))
        {
            // The script already controls its tray icon; copy it verbatim.
            return bytes;
        }

        // The directive is plain ASCII, so encoding it with the file's own encoding
        // keeps UTF-16 copies valid while the original body bytes are copied through
        // untouched (no decode/encode round-trip).
        var header = encoding.GetBytes(NoTrayIconDirective + LineTerminator);
        var output = new byte[bytes.Length + header.Length];
        Buffer.BlockCopy(bytes, 0, output, 0, preambleLength);
        Buffer.BlockCopy(header, 0, output, preambleLength, header.Length);
        Buffer.BlockCopy(
            bytes,
            preambleLength,
            output,
            preambleLength + header.Length,
            bytes.Length - preambleLength);
        return output;
    }

    public void Delete(string? tempPath)
    {
        if (string.IsNullOrEmpty(tempPath) || !IsInsideTempDirectory(tempPath))
        {
            return;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch (Exception)
        {
            // Best effort: the OS reclaims %LOCALAPPDATA%\DotAHK\Temp eventually.
        }
    }

    private bool IsInsideTempDirectory(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(_tempDirectory) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string BuildFileName(string scriptPath)
    {
        var name = Path.GetFileNameWithoutExtension(scriptPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "script";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        // Keep the original extension (.ahk / .ahk1 / .ahk2) so the copy is
        // recognizable, then add a unique suffix so concurrent runs never clash.
        var extension = Path.GetExtension(scriptPath);
        return $"{name}.{Guid.NewGuid():N}{extension}";
    }

    private static bool ContainsNoTrayIcon(ReadOnlySpan<byte> body, Encoding encoding)
    {
        if (body.IsEmpty)
        {
            return false;
        }

        var text = encoding.GetString(body);
        return text.Contains(NoTrayIconDirective, StringComparison.OrdinalIgnoreCase);
    }

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode, 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode, 2);
        }

        return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 0);
    }
}
