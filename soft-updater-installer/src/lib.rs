//! Логика нативного инсталлера, вынесенная в библиотеку,
//! чтобы её можно было покрыть тестами.
//!
//! `main.rs` — лишь тонкая обёртка: разобрал аргументы → позвал `run`.

use std::fs;
use std::io;
use std::process::Command;

pub mod args;
pub mod extract;
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
/// Возвращает ошибку, если распаковка или перезапуск провалились.
/// Неудачное удаление архива ошибкой НЕ считается (только варнинг).
pub fn run(args: &Args) -> io::Result<()> {
    let outcome = wait::wait_for_pid(args.wait_pid, args.wait_timeout);
    if outcome == WaitOutcome::TimedOutKilled {
        eprintln!(
            "[installer] timeout waiting for pid {}, killed it",
            args.wait_pid
        );
    }

    let stats = extract::extract_zip(&args.archive, &args.target)?;
    eprintln!("[installer] extracted {} file(s)", stats.files);

    if let Err(e) = fs::remove_file(&args.archive) {
        eprintln!("[installer] warning: failed to remove archive: {e}");
    }

    Command::new(&args.restart).spawn().map_err(|e| {
        io::Error::new(e.kind(), format!("failed to restart {:?}: {e}", args.restart))
    })?;

    Ok(())
}
