importScripts("browser-api.js", "injected.js");

const nativeHostName = "com.pejpass.browserhost";
const protocolVersion = 1;
const keepAliveAlarm = "pejpass-native-keepalive";
const allowedTypes = new Set([
  "ping",
  "launchApp",
  "unlockVault",
  "unlockWithHello",
  "lockVault",
  "getVaultStatus",
  "getCredentials",
  "searchCredentials",
  "fillCredential",
  "saveCredential",
  "copyToClipboard",
  "generatePassword"
]);

let port = null;
let nextRequestId = 1;
let cachedStatus = "unknown";
let cachedHelloReady = null;
let nativeHostError = "";
const pending = new Map();
const matchRefreshVersions = new Map();
const matchRefreshTimers = new Map();
const matchCounts = new Map();
const activeFillCounts = new Map();
const matchRefreshDebounceMs = 300;

// Last known vault state survives service-worker / event-page restarts. No secrets are stored.
const sessionStateArea = globalThis.pejpassBrowserApi.storage?.session;

function persistState() {
  if (!sessionStateArea) return;
  void sessionStateArea.set({
    pejpassState: { status: cachedStatus, helloReady: cachedHelloReady }
  }).catch(() => {});
}

async function restoreState() {
  if (!sessionStateArea) return;
  try {
    const stored = (await sessionStateArea.get("pejpassState"))?.pejpassState;
    if (cachedStatus !== "unknown" || !stored ||
        (stored.status !== "locked" && stored.status !== "unlocked")) return;

    const restored = stored.status;
    cachedStatus = restored;
    cachedHelloReady = typeof stored.helloReady === "boolean" ? stored.helloReady : null;
    void setToolbarVaultIcon(restored === "unlocked");

    // The vault may have changed while the worker was stopped, so confirm with the desktop app.
    try {
      const response = await sendNative({ type: "getVaultStatus", protocolVersion }, 6000);
      if (response?.type === "vaultStatus" && response.app === "PejPass" &&
          response.protocolVersion === protocolVersion &&
          (response.status === "locked" || response.status === "unlocked")) {
        applyStatus(response.status, response.helloReady === true);
      }
    } catch {
      if (cachedStatus === restored) applyStatus("unavailable");
      return;
    }

    if (cachedStatus === "unlocked") {
      const tabs = await globalThis.pejpassBrowserApi.tabs.query({ active: true });
      for (const tab of tabs) {
        if (Number.isInteger(tab.id)) scheduleRefreshMatchCount(tab.id);
      }
    }
  } catch {
    // Persisted state is only an optimisation; the native host remains the source of truth.
  }
}

const toolbarIconCache = new Map();

async function loadToolbarIcon(unlocked) {
  const cacheKey = unlocked ? "unlocked" : "locked";
  if (toolbarIconCache.has(cacheKey)) return toolbarIconCache.get(cacheKey);

  const response = await fetch(
    globalThis.pejpassBrowserApi.runtime.getURL("icons/PejPass128.png")
  );
  if (!response.ok) throw new Error("Unable to load toolbar icon.");
  const image = await createImageBitmap(await response.blob());

  try {
    const imageData = {};
    for (const size of [16, 32]) {
      const canvas = new OffscreenCanvas(size, size);
      const context = canvas.getContext("2d");
      if (!context) throw new Error("Unable to create toolbar icon canvas.");

      context.clearRect(0, 0, size, size);
      context.drawImage(image, 0, 0, size, size);

      if (!unlocked) {
        const unit = size / 32;
        const centerX = size - 8 * unit;
        const top = size - 13 * unit;
        context.strokeStyle = "#ffffff";
        context.lineWidth = Math.max(1.5, 2 * unit);
        context.beginPath();
        context.arc(centerX, top + 3 * unit, 3 * unit, Math.PI, 0);
        context.stroke();

        context.fillStyle = "#ffffff";
        context.fillRect(size - 13 * unit, size - 12 * unit, 10 * unit, 8 * unit);
        context.fillStyle = "#344055";
        context.beginPath();
        context.arc(centerX, size - 9 * unit, 1.3 * unit, 0, Math.PI * 2);
        context.fill();
        context.fillRect(centerX - 0.6 * unit, size - 9 * unit, 1.2 * unit, 2.5 * unit);
      }

      imageData[size] = context.getImageData(0, 0, size, size);
    }

    toolbarIconCache.set(cacheKey, imageData);
    return imageData;
  } finally {
    image.close();
  }
}

async function setToolbarVaultIcon(unlocked) {
  try {
    const imageData = await loadToolbarIcon(unlocked);
    await globalThis.pejpassBrowserApi.action.setIcon({ imageData });
  } catch {
    // The browser may be shutting down.
  }
}

function applyStatus(status, helloReady = cachedHelloReady) {
  if (!["locked", "unlocked", "unavailable", "unknown"].includes(status)) return;
  const statusChanged = status !== cachedStatus;
  const helloReadyChanged = helloReady !== cachedHelloReady;
  if (!statusChanged && !helloReadyChanged) return;

  cachedStatus = status;
  cachedHelloReady = helloReady;
  persistState();
  if (statusChanged) {
    void setToolbarVaultIcon(status === "unlocked");
    if (status !== "unlocked") {
      void globalThis.pejpassBrowserApi.tabs.query({}).then((tabs) => {
        for (const tab of tabs) {
          if (Number.isInteger(tab.id)) clearMatchBadge(tab.id);
        }
      }).catch(() => {});
    } else {
      void globalThis.pejpassBrowserApi.tabs.query({ active: true }).then((tabs) => {
        for (const tab of tabs) {
          if (Number.isInteger(tab.id)) scheduleRefreshMatchCount(tab.id);
        }
      }).catch(() => {});
    }
  }
  void globalThis.pejpassBrowserApi.runtime.sendMessage({
    type: "vaultStateChanged",
    status,
    helloReady: cachedHelloReady
  }).catch(() => {
    // No popup is open to receive the state change.
  });
}
function onNativeMessage(message) {
  if (message?.type === "hostHeartbeat") return;

  if (message?.type === "vaultStateChanged") {
    applyStatus(message.status);

    if (message.status === "locked" || message.status === "unlocked") {
      void sendNative({ type: "getVaultStatus", protocolVersion }, 6000)
        .then((response) => {
          if (response?.type === "vaultStatus" &&
              response.app === "PejPass" &&
              response.protocolVersion === protocolVersion &&
              (response.status === "locked" || response.status === "unlocked")) {
            applyStatus(response.status, response.helloReady === true);
          }
        })
        .catch(() => {
          // Keep the last known readiness if a transient refresh fails.
        });
    }
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


function renderMatchBadge(tabId) {
  if ((activeFillCounts.get(tabId) || 0) > 0) {
    void globalThis.pejpassBrowserApi.action.setBadgeText({ text: "…", tabId }).catch(() => {});
    void globalThis.pejpassBrowserApi.action.setBadgeBackgroundColor({ color: "#8caaee", tabId }).catch(() => {});
    void globalThis.pejpassBrowserApi.action.setTitle({ title: "PejPass — Filling credentials…", tabId }).catch(() => {});
    return;
  }

  const count = matchCounts.get(tabId) || 0;
  const text = count === 0 ? "" : count === 25 ? "25+" : String(count);
  void globalThis.pejpassBrowserApi.action.setBadgeText({ text, tabId }).catch(() => {});
  void globalThis.pejpassBrowserApi.action.setBadgeBackgroundColor({ color: "#8caaee", tabId }).catch(() => {});
  void globalThis.pejpassBrowserApi.action.setTitle({
    title: count === 0 ? "PejPass" : `PejPass — ${count === 25 ? "25+" : count} matching account${count === 1 ? "" : "s"}`,
    tabId
  }).catch(() => {});
}

function setMatchBadge(tabId, count) {
  matchCounts.set(tabId, Math.max(0, Math.min(count, 25)));
  renderMatchBadge(tabId);
}

function setFillInProgress(tabId, inProgress) {
  const current = activeFillCounts.get(tabId) || 0;
  if (inProgress) {
    activeFillCounts.set(tabId, current + 1);
  } else if (current <= 1) {
    activeFillCounts.delete(tabId);
  } else {
    activeFillCounts.set(tabId, current - 1);
  }
  renderMatchBadge(tabId);
}

function clearMatchBadge(tabId) {
  setMatchBadge(tabId, 0);
}

function scheduleRefreshMatchCount(tabId) {
  if (!Number.isInteger(tabId) || tabId < 0) return;
  const existingTimer = matchRefreshTimers.get(tabId);
  if (existingTimer !== undefined) clearTimeout(existingTimer);

  const timer = setTimeout(() => {
    matchRefreshTimers.delete(tabId);
    void refreshMatchCount(tabId);
  }, matchRefreshDebounceMs);
  matchRefreshTimers.set(tabId, timer);
}

async function refreshMatchCount(tabId) {
  if (!Number.isInteger(tabId) || tabId < 0) return;
  const version = (matchRefreshVersions.get(tabId) || 0) + 1;
  matchRefreshVersions.set(tabId, version);
  const previousCount = matchCounts.get(tabId) || 0;
  clearMatchBadge(tabId);

  try {
    const tab = await globalThis.pejpassBrowserApi.tabs.get(tabId);
    if (matchRefreshVersions.get(tabId) !== version || typeof tab.url !== "string") return;

    let url;
    try {
      url = new URL(tab.url);
    } catch {
      return;
    }

    if (url.protocol !== "http:" && url.protocol !== "https:") return;
    const domain = url.hostname.toLowerCase().replace(/\.$/, "");
    if (!domain) return;

    if (cachedStatus !== "unlocked") return;

    const response = await sendNative({ type: "getCredentials", domain }, 6_000);
    if (matchRefreshVersions.get(tabId) !== version) return;
    if (response?.type === "error" && response.code === "app_busy") {
      if (matchRefreshVersions.get(tabId) === version) setMatchBadge(tabId, previousCount);
      return;
    }
    if (response?.type !== "credentialMatches" || response.app !== "PejPass" ||
        response.protocolVersion !== protocolVersion || response.domain !== domain ||
        !Array.isArray(response.credentials) || response.credentials.length > 25) {
      return;
    }

    const currentTab = await globalThis.pejpassBrowserApi.tabs.get(tabId);
    if (matchRefreshVersions.get(tabId) !== version || typeof currentTab.url !== "string") return;
    const currentUrl = new URL(currentTab.url);
    if ((currentUrl.protocol !== "http:" && currentUrl.protocol !== "https:") ||
        currentUrl.hostname.toLowerCase().replace(/\.$/, "") !== domain) return;

    setMatchBadge(tabId, response.credentials.length);
  } catch {
    // Keep the badge empty if the tab, vault, or native host is unavailable.
  }
}

async function recordCredentialUse(credentialId) {
  try {
    const stored = await globalThis.pejpassBrowserApi.storage.local.get("lastUsedByCredential");
    const history = stored?.lastUsedByCredential && typeof stored.lastUsedByCredential === "object"
      ? stored.lastUsedByCredential
      : {};
    history[credentialId] = Date.now();
    const boundedHistory = Object.fromEntries(
      Object.entries(history)
        .filter(([, timestamp]) => Number.isFinite(Number(timestamp)) && Number(timestamp) > 0)
        .sort((left, right) => Number(right[1]) - Number(left[1]))
        .slice(0, 500)
    );
    await globalThis.pejpassBrowserApi.storage.local.set({ lastUsedByCredential: boundedHistory });
  } catch {
    // Filling should not fail if the browser cannot persist usage metadata.
  }
}

function ensurePort() {
  if (port) return port;

  let connectedPort;
  try {
    connectedPort = globalThis.pejpassBrowserApi.runtime.connectNative(nativeHostName);
  } catch (error) {
    nativeHostError = String(error?.message || error || "Native messaging host connection failed.");
    applyStatus("unavailable");
    throw error;
  }
  port = connectedPort;
  nativeHostError = "";

  connectedPort.onMessage.addListener(onNativeMessage);
  connectedPort.onDisconnect.addListener(() => {
    const reason = globalThis.pejpassBrowserApi.runtime.lastError?.message || "Native host disconnected.";
    nativeHostError = reason;
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

globalThis.pejpassBrowserApi.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== globalThis.pejpassBrowserApi.runtime.id) return false;

  if (message?.target === "pejpass-match-count") {
    const { tabId, count } = message;
    if (!Number.isInteger(tabId) || tabId < 0 ||
        !Number.isInteger(count) || count < 0 || count > 25) {
      sendResponse({ ok: false });
      return false;
    }

    setMatchBadge(tabId, count);
    sendResponse({ ok: true });
    return false;
  }

  if (message?.target === "pejpass-search-credentials") {
    const { tabId, domain, query } = message;
    if (!Number.isInteger(tabId) || tabId < 0 ||
        typeof domain !== "string" || domain.length === 0 || domain.length > 253 ||
        typeof query !== "string" || query.trim().length < 2 || query.length > 100) {
      sendResponse({ type: "error", code: "invalid_request" });
      return false;
    }

    sendNative({ type: "searchCredentials", domain, query: query.trim() }, 50_000)
      .then((response) => {
        if (response?.type === "error") {
          sendResponse(response);
          return;
        }
        if (response?.type !== "credentialSearchResults" || response.app !== "PejPass" ||
            response.protocolVersion !== protocolVersion || response.domain !== domain ||
            !Array.isArray(response.credentials) || response.credentials.length > 25 ||
            !response.credentials.every((credential) =>
              credential && typeof credential.id === "string" &&
              /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(credential.id) &&
              typeof credential.title === "string" && credential.title.length <= 200 &&
              typeof credential.username === "string" && credential.username.length <= 256 &&
              typeof credential.url === "string" && credential.url.length <= 2048)) {
          sendResponse({ type: "error", code: "invalid_app_response" });
          return;
        }
        sendResponse(response);
      })
      .catch(() => sendResponse({ type: "error", code: "host_unavailable" }));
    return true;
  }

  if (message?.target === "pejpass-fill") {
    const { tabId, domain, id } = message;
    if (!Number.isInteger(tabId) || tabId < 0 ||
        typeof domain !== "string" || domain.length === 0 || domain.length > 253 ||
        typeof id !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) {
      sendResponse({ type: "error", code: "invalid_request" });
      return false;
    }

    setFillInProgress(tabId, true);
    const allowDomainMismatch = message.allowDomainMismatch === true;
    sendNative({ type: "fillCredential", domain, id, allowDomainMismatch }, 65_000)
      .then(async (response) => {
        try {
          if (response?.type === "error") {
            sendResponse(response);
            return;
          }

          if (response?.type !== "credentialForFill" || response.app !== "PejPass" ||
              response.protocolVersion !== protocolVersion || response.domain !== domain ||
              response.id !== id || typeof response.username !== "string" ||
              response.username.length > 256 || typeof response.password !== "string" ||
              response.password.length > 4096 ||
              (response.totpCode != null && (typeof response.totpCode !== "string" ||
                response.totpCode.length > 8 ||
                (response.totpCode.length > 0 && !/^\d+$/.test(response.totpCode))))) {
            sendResponse({ type: "error", code: "invalid_app_response" });
            return;
          }

          let results;
          try {
            results = await globalThis.pejpassBrowserApi.scripting.executeScript({
              target: { tabId, allFrames: true },
              func: processLoginForm,
              args: ["fill", domain, response.username, response.password, response.totpCode || ""]
            });
          } catch {
            sendResponse({ type: "error", code: "fill_unavailable" });
            return;
          }
          const outcomes = (results || []).map((entry) => entry.result);
          const outcome = outcomes.includes("totp_filled") ? "totp_filled" :
            outcomes.includes("totp_unavailable") ? "totp_unavailable" :
            outcomes.includes("filled") ? "filled" :
            outcomes.includes("username_only") ? "username_only" :
            outcomes.includes("no_username") ? "no_username" :
            outcomes.includes("wrong_page") ? "wrong_page" : "no_password";

          if (["filled", "username_only", "totp_filled", "totp_unavailable", "no_username"].includes(outcome)) {
            await recordCredentialUse(id);
          }

          sendResponse({
            type: "fillCompleted",
            app: "PejPass",
            protocolVersion,
            domain,
            id,
            outcome
          });
        } finally {
          setFillInProgress(tabId, false);
          if (cachedStatus === "unlocked") scheduleRefreshMatchCount(tabId);
        }
      })
      .catch(() => {
        sendResponse({ type: "error", code: "host_unavailable" });
        setFillInProgress(tabId, false);
        if (cachedStatus === "unlocked") scheduleRefreshMatchCount(tabId);
      });

    return true;
  }

  if (message?.target === "pejpass-state") {
    ensureConnectedQuietly();
    sendResponse({
      status: cachedStatus,
      helloReady: cachedHelloReady,
      nativeHostError: nativeHostError || undefined
    });
    return false;
  }

  if (message?.target !== "pejpass-native") return false;

  const payload = message.payload;
  if (!payload || typeof payload.type !== "string" || !allowedTypes.has(payload.type)) {
    sendResponse({ type: "error", code: "unsupported_request" });
    return false;
  }

  const timeoutMs = payload.type === "unlockVault" || payload.type === "unlockWithHello" ? 70_000 :
    payload.type === "searchCredentials" ? 50_000 :
    payload.type === "fillCredential" ? 65_000 : 6_000;
  sendNative(payload, timeoutMs)
    .then((response) => {
      if (payload.type === "getVaultStatus" &&
          response?.type === "vaultStatus" &&
          response.app === "PejPass" &&
          response.protocolVersion === protocolVersion) {
        applyStatus(response.status, response.helloReady === true);
      } else if (payload.type === "unlockWithHello" && response?.code === "hello_not_ready") {
        applyStatus(cachedStatus, false);
      }
      sendResponse(response);
    })
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
  globalThis.pejpassBrowserApi.alarms.create(keepAliveAlarm, { periodInMinutes: 0.5 });
}

globalThis.pejpassBrowserApi.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (typeof changeInfo.url === "string") {
    const version = (matchRefreshVersions.get(tabId) || 0) + 1;
    matchRefreshVersions.set(tabId, version);
    clearMatchBadge(tabId);
  }

  if (changeInfo.status === "complete" && tab.active) {
    scheduleRefreshMatchCount(tabId);
  }
});

globalThis.pejpassBrowserApi.tabs.onActivated.addListener(({ tabId }) => {
  scheduleRefreshMatchCount(tabId);
});

globalThis.pejpassBrowserApi.tabs.onRemoved.addListener((tabId) => {
  const timer = matchRefreshTimers.get(tabId);
  if (timer !== undefined) clearTimeout(timer);
  matchRefreshTimers.delete(tabId);
  matchRefreshVersions.delete(tabId);
  matchCounts.delete(tabId);
  activeFillCounts.delete(tabId);
});

globalThis.pejpassBrowserApi.runtime.onInstalled.addListener(() => {
  scheduleReconnectAlarm();
  ensureConnectedQuietly();
});

globalThis.pejpassBrowserApi.runtime.onStartup.addListener(() => {
  scheduleReconnectAlarm();
  ensureConnectedQuietly();
  void globalThis.pejpassBrowserApi.tabs.query({ active: true, lastFocusedWindow: true }).then((tabs) => {
    for (const tab of tabs) if (Number.isInteger(tab.id)) scheduleRefreshMatchCount(tab.id);
  }).catch(() => {});
});

globalThis.pejpassBrowserApi.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === keepAliveAlarm) ensureConnectedQuietly();
});

void restoreState();
// Re-establish the native connection whenever the service worker starts.
ensureConnectedQuietly();
