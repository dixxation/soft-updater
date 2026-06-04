//! Простой логгер без внешних зависимостей: пишет строки с UTC-временем
//! одновременно в файл и в stderr.

use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};

pub struct Logger {
    file: Option<fs::File>,
    to_stderr: bool,
}

impl Logger {
    /// Логгер, пишущий в файл `path` (и дублирующий в stderr).
    /// Если файл открыть не удалось — молча остаёмся только на stderr.
    pub fn to_file(path: &Path) -> Logger {
        if let Some(parent) = path.parent() {
            let _ = fs::create_dir_all(parent);
        }
        let file = OpenOptions::new().create(true).append(true).open(path).ok();
        Logger { file, to_stderr: true }
    }

    /// Логгер, который никуда не пишет — для тестов.
    pub fn silent() -> Logger {
        Logger { file: None, to_stderr: false }
    }

    fn line(&mut self, level: &str, msg: &str) {
        let formatted = format!("{} [{level}] {msg}", now_utc());
        if self.to_stderr {
            eprintln!("{formatted}");
        }
        if let Some(f) = self.file.as_mut() {
            let _ = writeln!(f, "{formatted}");
            let _ = f.flush();
        }
    }

    pub fn info(&mut self, msg: &str) {
        self.line("INFO", msg);
    }
    pub fn warn(&mut self, msg: &str) {
        self.line("WARN", msg);
    }
    pub fn error(&mut self, msg: &str) {
        self.line("ERROR", msg);
    }
}

/// Текущее время UTC в виде "YYYY-MM-DD HH:MM:SS".
/// Считаем вручную, чтобы не тащить chrono ради одной строки.
fn now_utc() -> String {
    let secs = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);

    let days = (secs / 86_400) as i64;
    let rem = secs % 86_400;
    let (hour, min, sec) = (rem / 3600, (rem % 3600) / 60, rem % 60);

    // Алгоритм civil_from_days (Howard Hinnant).
    let z = days + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = doy - (153 * mp + 2) / 5 + 1;
    let m = if mp < 10 { mp + 3 } else { mp - 9 };
    let y = if m <= 2 { y + 1 } else { y };

    format!("{y:04}-{m:02}-{d:02} {hour:02}:{min:02}:{sec:02} UTC")
}
