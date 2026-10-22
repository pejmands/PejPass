const nativeHostName = "com.pejpass.browserhost";
const protocolVersion = 1;
const statusAlarmName = "refresh-vault-toolbar-icon";
const pollIntervalMinutes = 0.5;

async function setToolbarVaultIcon(unlocked) {
  const iconPath = unlocked ? "icons/PejPass.ico" : "icons/PejPassLocked.ico";
  await chrome.action.setIcon({
    path: {
      16: iconPath,
      32: iconPath
    }
  });
}

async function refreshToolbarVaultIcon() {
  try {
    const response = await chrome.runtime.sendNativeMessage(nativeHostName, {
      type: "getVaultStatus",
      protocolVersion
    });

    if (response?.type === "vaultStatus" &&
        response.app === "PejPass" &&
        response.protocolVersion === protocolVersion &&
        ["locked", "unlocked"].includes(response.status)) {
      await setToolbarVaultIcon(response.status === "unlocked");
      return;
    }

    // If the desktop app cannot confirm an unlocked vault, do not leave the unlocked icon visible.
    await setToolbarVaultIcon(false);
  } catch {
    // The desktop app or native host is unavailable; use the conservative locked icon.
    try {
      await setToolbarVaultIcon(false);
    } catch {
      // The browser may be shutting down.
    }
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
