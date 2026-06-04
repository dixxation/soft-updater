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

    eprintln!("[installer] archive:   {}", args.archive.display());
    eprintln!("[installer] target:    {}", args.target.display());
    eprintln!("[installer] restart:   {}", args.restart.display());
    eprintln!("[installer] wait-pid:  {}", args.wait_pid);
    eprintln!("[installer] timeout:   {:?}", args.wait_timeout);
    eprintln!("[installer] waiting for host process to exit...");

    if let Err(e) = run(&args) {
        eprintln!("[installer] ERROR: {e}");
        exit(1);
    }

    eprintln!("[installer] done");
}
