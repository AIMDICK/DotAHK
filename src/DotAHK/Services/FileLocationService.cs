using System.Diagnostics;
using System.IO;

namespace DotAHK.Services;

/// <summary>
/// Default <see cref="IFileLocationService"/>. Uses <c>explorer.exe /select,</c> to
/// open the containing folder with the exact script highlighted, falling back to
/// the folder itself when the file has been moved or deleted externally.
/// </summary>
public sealed class FileLocationService : IFileLocationService
{
    public bool RevealInExplorer(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            if (File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                });

                return true;
            }

            // The file was moved or deleted externally - reveal its folder instead.
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
            });

            return true;
        }
        catch (Exception)
        {
            // The file may have moved, be inaccessible, or Explorer failed to
            // launch. Report the failure to the caller instead of crashing.
            return false;
        }
    }
}
