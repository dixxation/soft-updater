use std::path::PathBuf;
use std::time::Duration;

/// Разобранные аргументы командной строки.
///
/// Инсталлер вызывается так:
/// ```text
/// soft-updater-installer \
///     --archive  /tmp/update.zip \
///     --target   /opt/myapp \
///     --restart  /opt/myapp/MyApp \
///     --wait-pid 12345 \
///     --wait-timeout-secs 30   (необязательный, по умолчанию 30)
/// ```
#[derive(Debug, Clone)]
pub struct Args {
    /// Путь к скачанному zip-архиву со сборкой.
    pub archive: PathBuf,
    /// Папка, КУДА распаковывать (корень архива ложится прямо сюда).
    pub target: PathBuf,
    /// Что запустить заново после обновления.
    pub restart: PathBuf,
    /// PID хост-процесса, смерти которого нужно дождаться.
    pub wait_pid: u32,
    /// Сколько ждать смерти хоста, прежде чем убить его силой.
    pub wait_timeout: Duration,
}

/// Таймаут ожидания хоста по умолчанию.
pub const DEFAULT_WAIT_TIMEOUT_SECS: u64 = 30;

impl Args {
    /// Разбирает аргументы. Принимает готовый срез (включая argv[0]),
    /// чтобы это можно было тестировать без обращения к окружению.
    ///
    /// Возвращает понятную ошибку текстом вместо паники — никаких
    /// падений на "флаг без значения".
    pub fn parse(argv: &[String]) -> Result<Args, String> {
        let mut archive: Option<PathBuf> = None;
        let mut target: Option<PathBuf> = None;
        let mut restart: Option<PathBuf> = None;
        let mut wait_pid: Option<u32> = None;
        let mut wait_timeout_secs: u64 = DEFAULT_WAIT_TIMEOUT_SECS;

        let mut i = 1; // argv[0] — это путь к самому бинарю, пропускаем
        while i < argv.len() {
            let flag = argv[i].as_str();
            // Хелпер: достать значение следующего аргумента или вернуть ошибку.
            let value = || -> Result<&String, String> {
                argv.get(i + 1)
                    .ok_or_else(|| format!("flag `{flag}` requires a value"))
            };

            match flag {
                "--archive" => {
                    archive = Some(PathBuf::from(value()?));
                    i += 2;
                }
                "--target" => {
                    target = Some(PathBuf::from(value()?));
                    i += 2;
                }
                "--restart" => {
                    restart = Some(PathBuf::from(value()?));
                    i += 2;
                }
                "--wait-pid" => {
                    let raw = value()?;
                    wait_pid = Some(
                        raw.parse::<u32>()
                            .map_err(|_| format!("invalid --wait-pid: `{raw}`"))?,
                    );
                    i += 2;
                }
                "--wait-timeout-secs" => {
                    let raw = value()?;
                    wait_timeout_secs = raw
                        .parse::<u64>()
                        .map_err(|_| format!("invalid --wait-timeout-secs: `{raw}`"))?;
                    i += 2;
                }
                other => return Err(format!("unknown argument: `{other}`")),
            }
        }

        Ok(Args {
            archive: archive.ok_or("--archive is required")?,
            target: target.ok_or("--target is required")?,
            restart: restart.ok_or("--restart is required")?,
            wait_pid: wait_pid.ok_or("--wait-pid is required")?,
            wait_timeout: Duration::from_secs(wait_timeout_secs),
        })
    }
}
