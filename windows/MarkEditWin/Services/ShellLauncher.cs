using System.IO;
using System.Runtime.InteropServices;

namespace MarkEditWin.Services;

/// <summary>
/// The only place in the app that hands a string to the operating system for execution.
///
/// Everything that reaches this class comes from document content or user scripts, so each entry
/// point validates its input first and then calls a Win32 shell API directly. No command line is
/// ever built and no process is ever created with a user supplied string, which keeps shell
/// metacharacters out of the picture entirely.
/// </summary>
public static class ShellLauncher
{
    private const int SwShowNormal = 1;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr ShellExecuteW(
        IntPtr hwnd,
        string? lpOperation,
        string lpFile,
        string? lpParameters,
        string? lpDirectory,
        int nShowCmd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(
        IntPtr pidlFolder,
        uint cidl,
        IntPtr[]? apidl,
        uint dwFlags);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    /// <summary>
    /// Opens a link in the default browser, or a local file with its associated application.
    /// </summary>
    public static bool TryOpen(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        // A well formed absolute URI is only accepted for schemes that cannot run code.
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return uri.Scheme switch
            {
                "http" or "https" or "mailto" => ShellOpen(uri.AbsoluteUri),
                _ => false,
            };
        }

        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return false;
        }

        return ShellOpen(Path.GetFullPath(target));
    }

    /// <summary>
    /// Shows a file in Explorer with the file selected, or opens the folder itself.
    /// </summary>
    public static bool TryReveal(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var pidl = IntPtr.Zero;

        try
        {
            if (SHParseDisplayName(fullPath, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
            {
                return false;
            }

            // The item is passed as a shell identifier, so no path parsing or quoting happens.
            return SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (pidl != IntPtr.Zero)
            {
                CoTaskMemFree(pidl);
            }
        }
    }

    /// <summary>Starts another copy of this executable.</summary>
    public static bool TryRelaunch()
    {
        var path = Environment.ProcessPath;
        return path is not null && ShellOpen(path);
    }

    private static bool ShellOpen(string validatedTarget)
    {
        try
        {
            var result = ShellExecuteW(IntPtr.Zero, "open", validatedTarget, null, null, SwShowNormal);
            return result.ToInt64() > 32;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
