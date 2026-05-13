use std::fs::{self, File};
use std::io;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::time::Duration;

mod args;
mod wait;

use args::Args;

fn main() {
    let args = Args::parse();

    eprintln!("[installer] archive:  {}", args.archive.display());
    eprintln!("[installer] target:   {}", args.target.display());
    eprintln!("[installer] restart:  {}", args.restart.display());
    eprintln!("[installer] wait-pid: {}", args.wait_pid);

    // 1. Ждём завершения основного процесса
    eprintln!("[installer] waiting for process {} to exit...", args.wait_pid);
    wait::wait_for_pid(args.wait_pid, Duration::from_secs(30));
    eprintln!("[installer] process exited");

    // 2. Распаковываем архив прямо в целевую директорию
    eprintln!("[installer] extracting archive...");
    if let Err(e) = extract_zip(&args.archive, &args.target) {
        eprintln!("[installer] ERROR: failed to extract: {e}");
        std::process::exit(1);
    }
    eprintln!("[installer] extraction complete");

    // 3. Удаляем временный архив
    if let Err(e) = fs::remove_file(&args.archive) {
        eprintln!("[installer] warning: failed to remove archive: {e}");
    }

    // 4. Перезапускаем приложение
    eprintln!("[installer] restarting {}", args.restart.display());
    if let Err(e) = Command::new(&args.restart).spawn() {
        eprintln!("[installer] ERROR: failed to restart: {e}");
        std::process::exit(1);
    }

    eprintln!("[installer] done");
}

fn extract_zip(archive: &Path, target: &Path) -> io::Result<()> {
    let file    = File::open(archive)?;
    let mut zip = zip::ZipArchive::new(file)
        .map_err(|e| io::Error::new(io::ErrorKind::InvalidData, e))?;

    for i in 0..zip.len() {
        let mut entry = zip.by_index(i)
            .map_err(|e| io::Error::new(io::ErrorKind::Other, e))?;

        // GitLab кладёт файлы в корневую папку внутри архива (ezcnc2-2.4.7/...)
        // Срезаем первый компонент пути чтобы распаковать плоско в target
        let relative = strip_top_component(entry.name());
        if relative.as_os_str().is_empty() {
            continue; // корневая папка — пропускаем
        }

        let out_path = target.join(&relative);

        if entry.is_dir() {
            fs::create_dir_all(&out_path)?;
        } else {
            if let Some(parent) = out_path.parent() {
                fs::create_dir_all(parent)?;
            }
            let mut out_file = File::create(&out_path)?;
            io::copy(&mut entry, &mut out_file)?;

            // Linux: восстанавливаем права исполнения если были в архиве
            #[cfg(unix)]
            set_permissions(&out_path, &entry);
        }
    }

    Ok(())
}

/// Срезает первый компонент пути: "app-2.4.7/bin/MyApp" → "bin/MyApp"
fn strip_top_component(name: &str) -> PathBuf {
    let mut components = Path::new(name).components();
    components.next(); // пропускаем первый
    components.as_path().to_path_buf()
}

#[cfg(unix)]
fn set_permissions(path: &Path, entry: &zip::ZipFile<impl std::io::Read>) {
    use std::os::unix::fs::PermissionsExt;
    if let Some(mode) = entry.unix_mode() {
        let _ = fs::set_permissions(path, fs::Permissions::from_mode(mode));
    }
}