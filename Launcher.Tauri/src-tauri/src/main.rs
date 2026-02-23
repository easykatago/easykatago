mod bridge_process;
mod commands;
mod models;
#[cfg(test)]
mod bridge_process_tests;
#[cfg(test)]
mod commands_tests;
#[cfg(test)]
mod main_tests;

use bridge_process::{AppState, BridgeProcess};

fn main() {
    build_app()
        .run(tauri::generate_context!())
        .expect("error while running launcher_tauri");
}

pub fn build_app() -> tauri::Builder<tauri::Wry> {
    tauri::Builder::default()
        .manage(AppState {
            bridge: BridgeProcess::from_environment(),
        })
        .invoke_handler(tauri::generate_handler![
            commands::ping,
            commands::settings_read,
            commands::settings_write,
            commands::profiles_read,
            commands::profiles_write,
            commands::install_run,
            commands::launch_run
        ])
}
