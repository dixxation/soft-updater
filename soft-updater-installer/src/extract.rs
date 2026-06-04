use std::fs::{self, File};
use std::io::{self, ErrorKind};
use std::path::Path;

use zip::ZipArchive;

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
/// Защита от "zip-slip": если запись в архиве пытается вылезти за
/// пределы `target` (через `../` или абсолютный путь), вся распаковка
/// падает с ошибкой.
pub fn extract_zip(archive: &Path, target: &Path) -> io::Result<ExtractStats> {
    let file = File::open(archive)?;
    let mut zip = ZipArchive::new(file).map_err(zip_to_io)?;

    let mut stats = ExtractStats::default();

    for i in 0..zip.len() {
        let mut entry = zip.by_index(i).map_err(zip_to_io)?;

        // enclosed_name возвращает безопасный относительный путь
        // или None, если архив пытается вырваться наружу.
        let Some(relative) = entry.enclosed_name() else {
            return Err(io::Error::new(
                ErrorKind::InvalidData,
                format!("unsafe path in archive: {}", entry.name()),
            ));
        };

        let out_path = target.join(&relative);

        if entry.is_dir() {
            fs::create_dir_all(&out_path)?;
        } else {
            if let Some(parent) = out_path.parent() {
                fs::create_dir_all(parent)?;
            }
            let mut out_file = File::create(&out_path)?;
            io::copy(&mut entry, &mut out_file)?;
            stats.files += 1;
        }
    }

    Ok(stats)
}

/// Ошибки zip-крейта — в обычную io::Error.
fn zip_to_io(e: zip::result::ZipError) -> io::Error {
    io::Error::new(ErrorKind::InvalidData, e)
}
