//! Тесты распаковки. Каждый тест сам собирает маленький zip
//! и проверяет, что файлы легли куда надо.

use std::fs;
use std::io::Write;
use std::path::Path;

use soft_updater_installer::extract::extract_zip;
use zip::write::SimpleFileOptions;
use zip::{CompressionMethod, ZipWriter};

/// Создаёт zip по списку (имя-внутри-архива, содержимое).
/// Без сжатия (Stored) — чтобы не тянуть лишние фичи в тестах.
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

/// Уникальная временная папка под каждый тест.
fn temp_dir(tag: &str) -> std::path::PathBuf {
    let dir = std::env::temp_dir().join(format!("sui-test-{tag}-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    dir
}

#[test]
fn extracts_root_layout_flat_and_nested() {
    let dir = temp_dir("root");
    let archive = dir.join("update.zip");
    let target = dir.join("install");
    fs::create_dir_all(&target).unwrap();

    // Корень-в-корень: файл прямо в корне + файл во вложенной папке.
    make_zip(
        &archive,
        &[
            ("MyApp.exe", b"binary-v2"),
            ("data/config.txt", b"hello"),
        ],
    );

    let stats = extract_zip(&archive, &target).unwrap();

    assert_eq!(stats.files, 2);
    assert_eq!(fs::read(target.join("MyApp.exe")).unwrap(), b"binary-v2");
    assert_eq!(
        fs::read(target.join("data/config.txt")).unwrap(),
        b"hello"
    );
}

#[test]
fn overwrites_existing_file() {
    let dir = temp_dir("overwrite");
    let archive = dir.join("update.zip");
    let target = dir.join("install");
    fs::create_dir_all(&target).unwrap();

    // Старая версия файла уже лежит на месте.
    fs::write(target.join("MyApp.exe"), b"binary-v1-OLD").unwrap();

    make_zip(&archive, &[("MyApp.exe", b"binary-v2-NEW")]);
    extract_zip(&archive, &target).unwrap();

    assert_eq!(
        fs::read(target.join("MyApp.exe")).unwrap(),
        b"binary-v2-NEW",
        "файл должен быть заменён новым содержимым"
    );
}

#[test]
fn rejects_zip_slip() {
    let dir = temp_dir("slip");
    let archive = dir.join("evil.zip");
    let target = dir.join("install");
    fs::create_dir_all(&target).unwrap();

    // Вредный архив пытается записать файл ВЫШЕ целевой папки.
    make_zip(&archive, &[("../escaped.txt", b"pwned")]);

    let result = extract_zip(&archive, &target);

    assert!(result.is_err(), "распаковка должна упасть на опасном пути");
    assert!(
        !dir.join("escaped.txt").exists(),
        "файл за пределами target не должен появиться"
    );
}
