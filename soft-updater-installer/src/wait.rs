use std::thread;
use std::time::{Duration, Instant};

/// Чем закончилось ожидание хост-процесса.
#[derive(Debug, PartialEq, Eq, Clone, Copy)]
pub enum WaitOutcome {
    /// Процесс завершился сам — штатный случай.
    Exited,
    /// Не дождались за таймаут, поэтому убили силой.
    TimedOutKilled,
}

/// Ждёт, пока процесс `pid` завершится, но не дольше `timeout`.
/// Если не дождались — убивает его и возвращает `TimedOutKilled`.
pub fn wait_for_pid(pid: u32, timeout: Duration) -> WaitOutcome {
    let deadline = Instant::now() + timeout;

    loop {
        if !is_running(pid) {
            return WaitOutcome::Exited;
        }
        if Instant::now() >= deadline {
            kill(pid);
            // Дать ОС время реально снять процесс, чтобы файлы освободились.
            thread::sleep(Duration::from_millis(500));
            return WaitOutcome::TimedOutKilled;
        }
        thread::sleep(Duration::from_millis(100));
    }
}

// ── Платформенные реализации ──────────────────────────────────────────────

#[cfg(target_os = "windows")]
pub fn is_running(pid: u32) -> bool {
    use std::ptr::null_mut;
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::System::Threading::{OpenProcess, WaitForSingleObject};

    const SYNCHRONIZE: u32 = 0x0010_0000;

    unsafe {
        let handle = OpenProcess(SYNCHRONIZE, 0, pid);
        if handle == null_mut() {
            return false;
        }
        let result = WaitForSingleObject(handle, 0);
        CloseHandle(handle);
        result != 0
    }
}

#[cfg(target_os = "windows")]
pub fn kill(pid: u32) {
    use std::ptr::null_mut;
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::System::Threading::{OpenProcess, TerminateProcess, PROCESS_TERMINATE};

    unsafe {
        let handle = OpenProcess(PROCESS_TERMINATE, 0, pid);
        if handle != null_mut() {
            TerminateProcess(handle, 1);
            CloseHandle(handle);
        }
    }
}

#[cfg(unix)]
pub fn is_running(pid: u32) -> bool {
    // kill(pid, 0) ничего не отправляет, только проверяет, что процесс есть.
    unsafe { libc::kill(pid as libc::pid_t, 0) == 0 }
}

#[cfg(unix)]
pub fn kill(pid: u32) {
    unsafe {
        libc::kill(pid as libc::pid_t, libc::SIGKILL);
    }
}
