const nativeHostName = "com.pejpass.browserhost";
const protocolVersion = 1;
const statusAlarmName = "refresh-vault-toolbar-icon";
const pollIntervalMinutes = 0.5;

const toolbarIconCache = new Map();

async function loadToolbarIcon(imagePath) {
  if (toolbarIconCache.has(imagePath)) {
    return toolbarIconCache.get(imagePath);
  }

  const image = new Image();
  image.src = chrome.runtime.getURL(imagePath);
  await image.decode();

  const imageData = {};
  for (const size of [16, 32]) {
    const canvas = new OffscreenCanvas(size, size);
    const context = canvas.getContext("2d");
    if (!context) {
      throw new Error("Unable to create toolbar icon canvas.");
    }

    context.clearRect(0, 0, size, size);
    context.drawImage(image, 0, 0, size, size);
    imageData[size] = context.getImageData(0, 0, size, size);
  }

  toolbarIconCache.set(imagePath, imageData);
  return imageData;
}

async function setToolbarVaultIcon(unlocked) {
  const imagePath = unlocked ? "icons/PejPass.ico" : "icons/PejPassLocked.ico";
  const imageData = await loadToolbarIcon(imagePath);
  await chrome.action.setIcon({ imageData });
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
