use std::thread;
use std::time::{Duration, Instant};

pub fn wait_for_pid(pid: u32, timeout: Duration) {
    let deadline = Instant::now() + timeout;

    loop {
        if !is_running(pid) {
            return;
        }
        if Instant::now() >= deadline {
            eprintln!("[installer] timeout waiting for pid {pid}, killing...");
            kill(pid);
            thread::sleep(Duration::from_millis(500));
            return;
        }
        thread::sleep(Duration::from_millis(200));
    }
}

#[cfg(target_os = "windows")]
fn is_running(pid: u32) -> bool {
    use std::ptr::null_mut;
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::System::Threading::{OpenProcess, WaitForSingleObject};

    const SYNCHRONIZE: u32 = 0x00100000;

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
fn kill(pid: u32) {
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
fn is_running(pid: u32) -> bool {
    unsafe { libc::kill(pid as libc::pid_t, 0) == 0 }
}

#[cfg(unix)]
fn kill(pid: u32) {
    unsafe {
        libc::kill(pid as libc::pid_t, libc::SIGKILL);
    }
}