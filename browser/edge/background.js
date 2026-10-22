const nativeHostName = "com.pejpass.browserhost";
const protocolVersion = 1;
const keepAliveAlarm = "pejpass-native-keepalive";
const allowedTypes = new Set([
  "ping",
  "launchApp",
  "unlockVault",
  "lockVault",
  "getVaultStatus",
  "getCredentials",
  "fillCredential",
  "saveCredential"
]);

let port = null;
let nextRequestId = 1;
let cachedStatus = "unknown";
const pending = new Map();

const toolbarIconCache = new Map();

async function loadToolbarIcon(imagePath) {
  if (toolbarIconCache.has(imagePath)) return toolbarIconCache.get(imagePath);

  const response = await fetch(chrome.runtime.getURL(imagePath));
  if (!response.ok) throw new Error("Unable to load toolbar icon.");
  const image = await createImageBitmap(await response.blob());

  const imageData = {};
  for (const size of [16, 32]) {
    const canvas = new OffscreenCanvas(size, size);
    const context = canvas.getContext("2d");
    if (!context) throw new Error("Unable to create toolbar icon canvas.");
    context.clearRect(0, 0, size, size);
    context.drawImage(image, 0, 0, size, size);
    imageData[size] = context.getImageData(0, 0, size, size);
  }

  image.close();
  toolbarIconCache.set(imagePath, imageData);
  return imageData;
}

async function setToolbarVaultIcon(unlocked) {
  try {
    const imageData = await loadToolbarIcon(
      unlocked ? "icons/PejPass.ico" : "icons/PejPassLocked.ico"
    );
    await chrome.action.setIcon({ imageData });
  } catch {
    // The browser may be shutting down.
  }
}

function applyStatus(status) {
  if (!["locked", "unlocked", "unavailable", "unknown"].includes(status)) return;
  if (status === cachedStatus) return;

  cachedStatus = status;
  void setToolbarVaultIcon(status === "unlocked");
  void chrome.runtime.sendMessage({ type: "vaultStateChanged", status }).catch(() => {
    // No popup is open to receive the state change.
  });
}

function onNativeMessage(message) {
  if (message?.type === "vaultStateChanged") {
    applyStatus(message.status);
    return;
  }

  const requestId = message?.requestId;
  const request = typeof requestId === "string" ? pending.get(requestId) : undefined;
  if (!request) return;

  clearTimeout(request.timer);
  pending.delete(requestId);
  const response = { ...message };
  delete response.requestId;
  request.resolve(response);
}

function ensurePort() {
  if (port) return port;

  const connectedPort = chrome.runtime.connectNative(nativeHostName);
  port = connectedPort;

  connectedPort.onMessage.addListener(onNativeMessage);
  connectedPort.onDisconnect.addListener(() => {
    const reason = chrome.runtime.lastError?.message || "Native host disconnected.";
    if (port === connectedPort) port = null;

    for (const [requestId, request] of pending) {
      clearTimeout(request.timer);
      request.reject(new Error(reason));
      pending.delete(requestId);
    }

    applyStatus("unavailable");
  });

  return connectedPort;
}

function sendNative(payload, timeoutMs) {
  return new Promise((resolve, reject) => {
    let activePort;
    try {
      activePort = ensurePort();
    } catch (error) {
      reject(error);
      return;
    }

    const requestId = String(nextRequestId++);
    const timer = setTimeout(() => {
      pending.delete(requestId);
      reject(new Error("Native host request timed out."));
    }, timeoutMs);

    pending.set(requestId, { resolve, reject, timer });

    try {
      activePort.postMessage({ ...payload, protocolVersion, requestId });
    } catch (error) {
      clearTimeout(timer);
      pending.delete(requestId);
      reject(error);
    }
  });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id) return false;

  if (message?.target === "pejpass-state") {
    ensureConnectedQuietly();
    sendResponse({ status: cachedStatus });
    return false;
  }

  if (message?.target !== "pejpass-native") return false;

  const payload = message.payload;
  if (!payload || typeof payload.type !== "string" || !allowedTypes.has(payload.type)) {
    sendResponse({ type: "error", code: "unsupported_request" });
    return false;
  }

  const timeoutMs = payload.type === "unlockVault" ? 50_000 : 6_000;
  sendNative(payload, timeoutMs)
    .then(sendResponse)
    .catch((error) => {
      sendResponse({
        type: "error",
        code: "host_unavailable",
        message: String(error?.message || error)
      });
    });

  return true;
});

function ensureConnectedQuietly() {
  try {
    ensurePort();
  } catch {
    applyStatus("unavailable");
  }
}

function scheduleReconnectAlarm() {
  chrome.alarms.create(keepAliveAlarm, { periodInMinutes: 0.5 });
}

chrome.runtime.onInstalled.addListener(() => {
  scheduleReconnectAlarm();
  ensureConnectedQuietly();
});

chrome.runtime.onStartup.addListener(() => {
  scheduleReconnectAlarm();
  ensureConnectedQuietly();
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === keepAliveAlarm) ensureConnectedQuietly();
});

// Re-establish the native connection whenever the service worker starts.
ensureConnectedQuietly();
