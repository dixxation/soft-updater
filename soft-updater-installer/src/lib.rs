//! Логика нативного инсталлера, вынесенная в библиотеку,
//! чтобы её можно было покрыть тестами (см. папку `tests/`).
//!
//! `main.rs` — лишь тонкая обёртка: разобрал аргументы → позвал `run`.

use std::fs;
use std::io;
use std::process::Command;

pub mod args;
pub mod extract;
pub mod log;
pub mod wait;

pub use args::Args;
pub use extract::ExtractStats;
pub use wait::WaitOutcome;

/// Полный сценарий обновления:
/// 1) дождаться смерти хоста (или убить по таймауту),
/// 2) распаковать архив в целевую папку,
/// 3) удалить архив,
/// 4) перезапустить приложение.
///
/// Каждый шаг и любая ошибка пишутся в лог-файл (по умолчанию
/// `<target>/soft-updater-installer.log`, либо путь из `--log`).
pub fn run(args: &Args) -> io::Result<()> {
    let log_path = args
        .log
        .clone()
        .unwrap_or_else(|| args.target.join("soft-updater-installer.log"));
    let mut log = log::Logger::to_file(&log_path);

    log.info("=== soft-updater-installer started ===");
    log.info(&format!("archive:  {}", args.archive.display()));
    log.info(&format!("target:   {}", args.target.display()));
    log.info(&format!("restart:  {}", args.restart.display()));
    log.info(&format!(
        "wait-pid: {} (timeout {:?})",
        args.wait_pid, args.wait_timeout
    ));

    log.info(&format!(
        "waiting for host process {} to exit...",
        args.wait_pid
    ));
    match wait::wait_for_pid(args.wait_pid, args.wait_timeout) {
        WaitOutcome::Exited => log.info("host process exited on its own"),
        WaitOutcome::TimedOutKilled => log.warn(&format!(
            "host did not exit within {:?}, killed it",
            args.wait_timeout
        )),
    }

    log.info("extracting archive...");
    let stats = match extract::extract_zip(&args.archive, &args.target, &mut log) {
        Ok(s) => s,
        Err(e) => {
            log.error(&format!("extraction FAILED: {e}"));
            return Err(e);
        }
    };
    log.info(&format!("extraction done: {} file(s) written", stats.files));

    match fs::remove_file(&args.archive) {
        Ok(()) => log.info("temporary archive removed"),
        Err(e) => log.warn(&format!("could not remove archive: {e}")),
    }

    log.info(&format!("restarting application: {}", args.restart.display()));
    match Command::new(&args.restart).spawn() {
        Ok(_) => log.info("application restarted"),
        Err(e) => {
            log.error(&format!("restart FAILED ({:?}): {e}", args.restart));
            return Err(io::Error::new(
                e.kind(),
                format!("failed to restart {:?}: {e}", args.restart),
            ));
        }
    }

    log.info("=== done ===");
    Ok(())
}
