using System.Diagnostics;
using System.Runtime.InteropServices;

namespace soft_updater_lib.Installer;

internal static class InstallerRunner
{
    private const string InstallerName = "soft-updater-installer";

    /// <summary>
    /// Находит инсталлер рядом с exe, запускает его и завершает текущий процесс.
    /// Инсталлер сам дождётся смерти PID, распакует архив и перезапустит приложение.
    /// </summary>
    public static void LaunchAndExit(string archivePath, string targetDirectory)
    {
        var installerPath = FindInstaller();

        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine current executable path");

        var currentPid = Process.GetCurrentProcess().Id;

        var args = string.Join(" ",
            "--archive",  Quote(archivePath),
            "--target",   Quote(targetDirectory),
            "--restart",  Quote(currentExe),
            "--wait-pid", currentPid.ToString()
        );

        var psi = new ProcessStartInfo
        {
            FileName        = installerPath,
            Arguments       = args,
            UseShellExecute = false,
            CreateNoWindow  = true,
        };

        _ = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start installer process");

        // Завершаем себя — инсталлер возьмёт управление
        Environment.Exit(0);
    }

    private static string FindInstaller()
    {
        var baseDir   = AppContext.BaseDirectory;
        var fileName  = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{InstallerName}.exe"
            : InstallerName;

        var path = Path.Combine(baseDir, fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Installer not found at '{path}'. " +
                $"Make sure '{fileName}' is placed next to the application executable.",
                path);

        // Linux: убедимся что файл исполняемый
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SetExecutable(path);

        return path;
    }

    private static void SetExecutable(string path)
    {
        // chmod +x через встроенный Unix API
        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute);
    }

    private static string Quote(string s) => $"\"{s}\"";
}