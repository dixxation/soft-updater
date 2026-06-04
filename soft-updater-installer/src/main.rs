use std::process::exit;

use soft_updater_installer::{run, Args};

fn main() {
    let argv: Vec<String> = std::env::args().collect();

    let args = match Args::parse(&argv) {
        Ok(a) => a,
        Err(e) => {
            eprintln!("[installer] argument error: {e}");
            exit(2);
        }
    };

    // Всё остальное (шаги, ошибки) пишется внутри run в лог-файл и stderr.
    if let Err(_e) = run(&args) {
        exit(1);
    }
}
