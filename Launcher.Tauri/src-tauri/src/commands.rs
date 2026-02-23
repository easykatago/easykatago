use serde_json::Value;

use crate::bridge_process::AppState;

#[tauri::command]
pub fn ping() -> &'static str {
    "easykatago"
}

#[tauri::command]
pub async fn settings_read(state: tauri::State<'_, AppState>) -> Result<Value, String> {
    state.bridge.invoke("settings.read", None).await
}

#[tauri::command]
pub async fn settings_write(
    state: tauri::State<'_, AppState>,
    payload: Value,
) -> Result<Value, String> {
    state.bridge.invoke("settings.write", Some(payload)).await
}

#[tauri::command]
pub async fn profiles_read(state: tauri::State<'_, AppState>) -> Result<Value, String> {
    state.bridge.invoke("profiles.read", None).await
}

#[tauri::command]
pub async fn profiles_write(
    state: tauri::State<'_, AppState>,
    payload: Value,
) -> Result<Value, String> {
    state.bridge.invoke("profiles.write", Some(payload)).await
}

#[tauri::command]
pub async fn install_run(state: tauri::State<'_, AppState>) -> Result<Value, String> {
    state.bridge.invoke("install.run", None).await
}

#[tauri::command]
pub async fn launch_run(state: tauri::State<'_, AppState>) -> Result<Value, String> {
    state.bridge.invoke("launch.run", None).await
}
