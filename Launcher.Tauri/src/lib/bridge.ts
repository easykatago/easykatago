import { invoke } from "@tauri-apps/api/core";
import type { ProfilesData, SettingsData } from "./contracts";

export async function settingsRead(): Promise<SettingsData> {
  return invoke("settings_read");
}

export async function settingsWrite(payload: SettingsData): Promise<SettingsData> {
  return invoke("settings_write", { payload });
}

export async function profilesRead(): Promise<ProfilesData> {
  return invoke("profiles_read");
}

export async function profilesWrite(payload: ProfilesData): Promise<ProfilesData> {
  return invoke("profiles_write", { payload });
}

export async function installRun(): Promise<{ status: string }> {
  return invoke("install_run");
}

export async function launchRun(): Promise<{ status: string }> {
  return invoke("launch_run");
}
