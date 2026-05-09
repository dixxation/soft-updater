use std::path::PathBuf;

pub struct Args {
    pub archive:  PathBuf,
    pub target:   PathBuf,
    pub restart:  PathBuf,
    pub wait_pid: u32,
}

impl Args {
    pub fn parse() -> Self {
        let args: Vec<String> = std::env::args().collect();
        let mut archive  = None;
        let mut target   = None;
        let mut restart  = None;
        let mut wait_pid = None;

        let mut i = 1;
        while i < args.len() {
            match args[i].as_str() {
                "--archive"  => { archive  = Some(PathBuf::from(&args[i + 1])); i += 2; }
                "--target"   => { target   = Some(PathBuf::from(&args[i + 1])); i += 2; }
                "--restart"  => { restart  = Some(PathBuf::from(&args[i + 1])); i += 2; }
                "--wait-pid" => { wait_pid = Some(args[i + 1].parse().expect("invalid PID")); i += 2; }
                _ => { i += 1; }
            }
        }

        Self {
            archive:  archive.expect("--archive is required"),
            target:   target.expect("--target is required"),
            restart:  restart.expect("--restart is required"),
            wait_pid: wait_pid.expect("--wait-pid is required"),
        }
    }
}