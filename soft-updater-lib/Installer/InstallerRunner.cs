using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace soft_updater_lib.Installer;

internal static class InstallerRunner
{
    private const string InstallerName = "soft-updater-installer";

    public static void LaunchAndExit(string archivePath, string targetDirectory)
    {
        var installerPath = ExtractInstaller();

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

        Environment.Exit(0);
    }

    /// <summary>
    /// Извлекает инсталлер из EmbeddedResource во временную папку.
    /// Если файл там уже есть и не устарел — переиспользует его.
    /// </summary>
    private static string ExtractInstaller()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var fileName  = isWindows ? $"{InstallerName}.exe" : InstallerName;

        // Кладём рядом с exe приложения — туда у нас точно есть доступ на запись
        var destPath = Path.Combine(AppContext.BaseDirectory, fileName);

        var resourceName = isWindows
            ? $"soft_updater_lib.Resources.{InstallerName}.exe"
            : $"soft_updater_lib.Resources.{InstallerName}";

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            // Embedded resource не найден — fallback на файл рядом с exe (dev окружение)
            if (File.Exists(destPath))
                return destPath;

            throw new FileNotFoundException(
                $"Installer resource '{resourceName}' not found in assembly. " +
                $"In development, place '{fileName}' next to the executable.",
                destPath);
        }

        // Перезаписываем каждый раз — гарантирует актуальную версию инсталлера
        using var dest = File.Create(destPath);
        stream.CopyTo(dest);

        if (!isWindows)
            File.SetUnixFileMode(destPath,
                UnixFileMode.UserExecute  | UnixFileMode.UserRead  | UnixFileMode.UserWrite |
                UnixFileMode.GroupExecute | UnixFileMode.GroupRead |
                UnixFileMode.OtherExecute | UnixFileMode.OtherRead);

        return destPath;
    }

    private static string Quote(string s) => $"\"{s}\"";
}