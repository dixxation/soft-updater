use std::fs::{self, File};
use std::io::{self, ErrorKind};
use std::path::Path;

use zip::ZipArchive;

use crate::log::Logger;

/// Что получилось в результате распаковки — для логов и для тестов.
#[derive(Debug, Default, PartialEq, Eq)]
pub struct ExtractStats {
    /// Сколько файлов (не папок) реально записано.
    pub files: usize,
}

/// Распаковывает архив `archive` прямо в папку `target`.
///
/// Раскладка "корень-в-корень": что лежит в корне архива — то и
/// окажется в корне `target`.
///
/// Защита от "zip-slip": если запись пытается вылезти за пределы
/// `target` (через `../` или абсолютный путь), вся распаковка падает
/// с ошибкой
///
/// В `log` пишется каждый распакованный файл и точная причина любой
/// ошибки — чтобы потом было видно, на чём именно всё встало.
pub fn extract_zip(archive: &Path, target: &Path, log: &mut Logger) -> io::Result<ExtractStats> {
    let file = File::open(archive).map_err(|e| {
        log.error(&format!("cannot open archive {}: {e}", archive.display()));
        e
    })?;
    let mut zip = ZipArchive::new(file).map_err(|e| {
        log.error(&format!("cannot read zip {}: {e}", archive.display()));
        zip_to_io(e)
    })?;

    let mut stats = ExtractStats::default();

    for i in 0..zip.len() {
        let mut entry = zip.by_index(i).map_err(zip_to_io)?;

        // enclosed_name отдаёт безопасный относительный путь
        // или None, если архив пытается вырваться наружу.
        let Some(relative) = entry.enclosed_name() else {
            log.error(&format!("unsafe path in archive, aborting: {}", entry.name()));
            return Err(io::Error::new(
                ErrorKind::InvalidData,
                format!("unsafe path in archive: {}", entry.name()),
            ));
        };

        let out_path = target.join(&relative);

        if entry.is_dir() {
            fs::create_dir_all(&out_path).map_err(|e| {
                log.error(&format!("cannot create dir {}: {e}", out_path.display()));
                e
            })?;
        } else {
            if let Some(parent) = out_path.parent() {
                fs::create_dir_all(parent).map_err(|e| {
                    log.error(&format!("cannot create dir {}: {e}", parent.display()));
                    e
                })?;
            }
            let mut out_file = File::create(&out_path).map_err(|e| {
                log.error(&format!("cannot write {}: {e}", out_path.display()));
                e
            })?;
            io::copy(&mut entry, &mut out_file).map_err(|e| {
                log.error(&format!("cannot write {}: {e}", out_path.display()));
                e
            })?;
            log.info(&format!("  + {}", relative.display()));
            stats.files += 1;
        }
    }

    Ok(stats)
}

/// Ошибки zip-крейта — в обычную io::Error, чтобы наружу шёл один тип.
fn zip_to_io(e: zip::result::ZipError) -> io::Error {
    io::Error::new(ErrorKind::InvalidData, e)
}
