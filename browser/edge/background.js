const nativeHostName = "com.pejpass.browserhost";
const protocolVersion = 1;
const statusAlarmName = "refresh-vault-toolbar-icon";
const pollIntervalMinutes = 0.5;

async function refreshToolbarVaultIcon() {
  try {
    const response = await chrome.runtime.sendNativeMessage(nativeHostName, {
      type: "getVaultStatus",
      protocolVersion
    });

    if (response?.type !== "vaultStatus" ||
        response.app !== "PejPass" ||
        response.protocolVersion !== protocolVersion ||
        !["locked", "unlocked"].includes(response.status)) {
      return;
    }

    const iconPath = response.status === "unlocked"
      ? "icons/PejPass.ico"
      : "icons/PejPassLocked.ico";

    await chrome.action.setIcon({
      path: {
        16: iconPath,
        32: iconPath
      }
    });
  } catch {
    // Keep the last confirmed icon when the desktop app is unavailable.
  }
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(statusAlarmName, { periodInMinutes: pollIntervalMinutes });
  void refreshToolbarVaultIcon();
});

chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create(statusAlarmName, { periodInMinutes: pollIntervalMinutes });
  void refreshToolbarVaultIcon();
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === statusAlarmName) {
    void refreshToolbarVaultIcon();
  }
});
