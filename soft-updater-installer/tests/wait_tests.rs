//! Тесты ожидания процесса. Запускаем настоящий процесс-пустышку
//! (бинарь test_helper) и проверяем обе ветки: сам вышел / убили по таймауту.
//! Кроссплатформенно — гоняются и на Windows, и на Linux/Mac.
//!
//! Нюанс на Unix: процесс-пустышка — наш ребёнок. Когда ребёнок умирает,
//! но его не "пожали" (wait), он остаётся зомби, и проверка "жив ли он"
//! всё ещё говорит "да". В бою хост инсталлеру не ребёнок — его пожинает
//! система. Поэтому тут мы пожинаем зомби руками.

use std::process::Command;
use std::time::{Duration, Instant};

use soft_updater_installer::wait::{is_running, wait_for_pid, WaitOutcome};

fn spawn_sleeper(secs: u32) -> std::process::Child {
    Command::new(env!("CARGO_BIN_EXE_test_helper"))
        .arg(secs.to_string())
        .spawn()
        .unwrap()
}

#[test]
fn returns_exited_when_process_dies_on_its_own() {
    let child = spawn_sleeper(1);
    let pid = child.id();

    // Фоновый "могильщик": дождётся смерти процесса и пожнёт зомби,
    // чтобы система перестала считать его существующим.
    std::thread::spawn(move || {
        let mut child = child;
        let _ = child.wait();
    });

    let start = Instant::now();
    let outcome = wait_for_pid(pid, Duration::from_secs(10));
    let elapsed = start.elapsed();

    assert_eq!(outcome, WaitOutcome::Exited);
    assert!(
        elapsed < Duration::from_secs(5),
        "должны вернуться вскоре после смерти процесса, а не ждать таймаут"
    );
}

#[test]
fn times_out_and_kills_a_stubborn_process() {
    let mut child = spawn_sleeper(60);
    let pid = child.id();

    let start = Instant::now();
    let outcome = wait_for_pid(pid, Duration::from_secs(1)); // тут процесс будет убит
    let elapsed = start.elapsed();

    // Пожинаем убитого зомби (в бою это сделала бы система).
    let _ = child.wait();

    assert_eq!(outcome, WaitOutcome::TimedOutKilled);
    assert!(
        elapsed < Duration::from_secs(3),
        "таймаут 1с + полсекунды на добивание — не должно занять 3с"
    );
    assert!(!is_running(pid), "процесс должен быть мёртв после kill");
}
