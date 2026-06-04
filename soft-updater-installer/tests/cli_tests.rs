//! Сквозной тест: запускаем НАСТОЯЩИЙ собранный бинарь инсталлера.
//! Это ровно тот сценарий, который ты описывал:
//!   - есть мусорный процесс (хост),
//!   - есть архив с новым файлом,
//!   - инсталлер ждёт смерти хоста, заменяет файл, удаляет архив,
//!     перезапускает приложение.
//! Кроссплатформенно: и на Windows, и на Linux/Mac.

use std::fs;
use std::io::Write;
use std::path::Path;
use std::process::Command;
use std::time::{Duration, Instant};

use zip::write::SimpleFileOptions;
use zip::{CompressionMethod, ZipWriter};

fn make_zip(path: &Path, entries: &[(&str, &[u8])]) {
    let file = fs::File::create(path).unwrap();
    let mut zip = ZipWriter::new(file);
    let opts = SimpleFileOptions::default().compression_method(CompressionMethod::Stored);
    for (name, content) in entries {
        zip.start_file(*name, opts).unwrap();
        zip.write_all(content).unwrap();
    }
    zip.finish().unwrap();
}

#[test]
fn full_update_pipeline() {
    // Папка под весь тест.
    let dir = std::env::temp_dir().join(format!("sui-e2e-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();

    let target = dir.join("install");
    fs::create_dir_all(&target).unwrap();

    // 1. Старая версия приложения уже установлена.
    fs::write(target.join("MyApp"), b"OLD-v1").unwrap();

    // 2. Архив с новой версией.
    let archive = dir.join("update.zip");
    make_zip(&archive, &[("MyApp", b"NEW-v2")]);

    // 3. "Перезапуск" — бинарь test_helper в режиме маркера. Путь маркера
    //    передаём через переменную окружения (она унаследуется: тест →
    //    инсталлер → restart). Появился маркер — значит restart выстрелил.
    let marker = dir.join("restarted.marker");
    let restart = env!("CARGO_BIN_EXE_test_helper");

    // 4. Мусорный (хост) процесс — спит, мы его убьём вручную.
    let mut host = Command::new(env!("CARGO_BIN_EXE_test_helper"))
        .arg("30")
        .spawn()
        .unwrap();
    let host_pid = host.id();

    // 5. Запускаем настоящий бинарь инсталлера.
    let installer = env!("CARGO_BIN_EXE_soft-updater-installer");
    let mut child = Command::new(installer)
        .args([
            "--archive",
            archive.to_str().unwrap(),
            "--target",
            target.to_str().unwrap(),
            "--restart",
            restart,
            "--wait-pid",
            &host_pid.to_string(),
            "--wait-timeout-secs",
            "10",
        ])
        .env("SUI_TEST_MARKER", &marker) // унаследуется в restart
        .spawn()
        .unwrap();

    // 6. Имитируем закрытие приложения — убиваем хост.
    std::thread::sleep(Duration::from_millis(300));
    let _ = host.kill();
    let _ = host.wait();

    // 7. Ждём, пока инсталлер отработает.
    let status = child.wait().unwrap();
    assert!(status.success(), "инсталлер должен завершиться кодом 0");

    // 8. Проверяем результат.
    assert_eq!(
        fs::read(target.join("MyApp")).unwrap(),
        b"NEW-v2",
        "файл должен быть заменён новой версией"
    );
    assert!(!archive.exists(), "временный архив должен быть удалён");

    // restart мог стартовать чуть позже — подождём появления маркера.
    let deadline = Instant::now() + Duration::from_secs(3);
    while !marker.exists() && Instant::now() < deadline {
        std::thread::sleep(Duration::from_millis(50));
    }
    assert!(marker.exists(), "приложение должно было перезапуститься");
}
