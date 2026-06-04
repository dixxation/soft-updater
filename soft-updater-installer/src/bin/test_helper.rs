//! Вспомогательный бинарь ТОЛЬКО для тестов. В релиз не попадает
//! (build.sh копирует лишь soft-updater-installer).
//!
//! Два режима, выбор по аргументу:
//!   test_helper 30      → спит 30 секунд (изображает хост-процесс)
//!   test_helper         → создаёт файл-маркер по пути из переменной
//!                         окружения SUI_TEST_MARKER (изображает рестарт)
//!
//! Нужен, чтобы тесты были одинаковыми на Windows и Linux и не зависели
//! от системных `sleep`/`timeout`.

use std::time::Duration;

fn main() {
    match std::env::args().nth(1).and_then(|s| s.parse::<u64>().ok()) {
        Some(secs) => std::thread::sleep(Duration::from_secs(secs)),
        None => {
            if let Ok(path) = std::env::var("SUI_TEST_MARKER") {
                let _ = std::fs::write(path, b"restarted");
            }
        }
    }
}
