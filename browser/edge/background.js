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
  "fillCredential",
  "saveCredential",
  "copyToClipboard"
]);

let port = null;
let nextRequestId = 1;
let cachedStatus = "unknown";
const pending = new Map();
const matchRefreshVersions = new Map();

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
  if (status !== "unlocked") {
    void chrome.tabs.query({}).then((tabs) => {
      for (const tab of tabs) {
        if (Number.isInteger(tab.id)) clearMatchBadge(tab.id);
      }
    }).catch(() => {});
  }
  void chrome.runtime.sendMessage({ type: "vaultStateChanged", status }).catch(() => {
    // No popup is open to receive the state change.
  });
}

function onNativeMessage(message) {
  if (message?.type === "hostHeartbeat") return;

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


function setMatchBadge(tabId, count) {
  const text = count === 0 ? "" : count === 25 ? "25+" : String(count);
  void chrome.action.setBadgeText({ text, tabId }).catch(() => {});
  void chrome.action.setBadgeBackgroundColor({ color: "#8caaee", tabId }).catch(() => {});
  void chrome.action.setTitle({
    title: count === 0 ? "PejPass" : `PejPass — ${count === 25 ? "25+" : count} matching account${count === 1 ? "" : "s"}`,
    tabId
  }).catch(() => {});
}

function clearMatchBadge(tabId) {
  setMatchBadge(tabId, 0);
}

async function refreshMatchCount(tabId) {
  if (!Number.isInteger(tabId) || tabId < 0) return;
  const version = (matchRefreshVersions.get(tabId) || 0) + 1;
  matchRefreshVersions.set(tabId, version);
  clearMatchBadge(tabId);

  try {
    const tab = await chrome.tabs.get(tabId);
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

    const status = await sendNative({ type: "getVaultStatus" }, 6_000);
    if (matchRefreshVersions.get(tabId) !== version) return;
    if (status?.type !== "vaultStatus" || status.app !== "PejPass" ||
        status.protocolVersion !== protocolVersion ||
        !["locked", "unlocked"].includes(status.status)) {
      return;
    }

    applyStatus(status.status);
    if (status.status !== "unlocked") return;

    const response = await sendNative({ type: "getCredentials", domain }, 6_000);
    if (matchRefreshVersions.get(tabId) !== version) return;
    if (response?.type !== "credentialMatches" || response.app !== "PejPass" ||
        response.protocolVersion !== protocolVersion || response.domain !== domain ||
        !Array.isArray(response.credentials) || response.credentials.length > 25) {
      return;
    }

    const currentTab = await chrome.tabs.get(tabId);
    if (matchRefreshVersions.get(tabId) !== version || typeof currentTab.url !== "string") return;
    const currentUrl = new URL(currentTab.url);
    if ((currentUrl.protocol !== "http:" && currentUrl.protocol !== "https:") ||
        currentUrl.hostname.toLowerCase().replace(/\.$/, "") !== domain) return;

    setMatchBadge(tabId, response.credentials.length);
  } catch {
    // Keep the badge empty if the tab, vault, or native host is unavailable.
  }
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

  if (message?.target === "pejpass-fill") {
    const { tabId, domain, id } = message;
    if (!Number.isInteger(tabId) || tabId < 0 ||
        typeof domain !== "string" || domain.length === 0 || domain.length > 253 ||
        typeof id !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) {
      sendResponse({ type: "error", code: "invalid_request" });
      return false;
    }

    sendNative({ type: "fillCredential", domain, id }, 65_000)
      .then(async (response) => {
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
          results = await chrome.scripting.executeScript({
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

        sendResponse({
          type: "fillCompleted",
          app: "PejPass",
          protocolVersion,
          domain,
          id,
          outcome
        });
      })
      .catch(() => sendResponse({ type: "error", code: "host_unavailable" }));

    return true;
  }


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

  const timeoutMs = payload.type === "unlockVault" || payload.type === "unlockWithHello" ? 50_000 :
    payload.type === "fillCredential" ? 65_000 : 6_000;
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

function processLoginForm(action, expectedDomain, username = "", password = "", totpCode = "") {
  const normalizeHost = (host) => host.toLowerCase().replace(/\.$/, "");
  const currentDomain = normalizeHost(window.location.hostname);
  let topDomain;
  try {
    topDomain = normalizeHost(window.top.location.hostname);
  } catch {
    return action === "read" ? { found: false } : "wrong_page";
  }

  if (topDomain !== expectedDomain ||
      (currentDomain !== expectedDomain && !currentDomain.endsWith("." + expectedDomain))) {
    return action === "read" ? { found: false } : "wrong_page";
  }

  const isVisible = (element) => {
    const style = window.getComputedStyle(element);
    return style.display !== "none" && style.visibility !== "hidden" &&
      element.getClientRects().length > 0;
  };
  const usable = (input) => !input.disabled && !input.readOnly && isVisible(input);
  const collectInputs = (root) => {
    const found = Array.from(root.querySelectorAll("input"));
    for (const element of root.querySelectorAll("*")) {
      if (element.shadowRoot) found.push(...collectInputs(element.shadowRoot));
    }
    return found;
  };
  const inputs = collectInputs(document).filter(usable);
  const passwordHints = (input) => [
    input.name, input.id, input.placeholder, input.getAttribute("aria-label"),
    input.getAttribute("data-testid"), input.getAttribute("data-field")
  ].filter(Boolean).join(" ");
  const isPasswordField = (input) => {
    const type = (input.type || "").toLowerCase();
    const autocomplete = (input.autocomplete || "").toLowerCase().split(/\s+/);
    return type === "password" ||
      autocomplete.includes("current-password") ||
      autocomplete.includes("new-password") ||
      /\b(pass(word|wd)?|pwd)\b/i.test(passwordHints(input));
  };
  const isTotpField = (input) => {
    const autocomplete = (input.autocomplete || "").toLowerCase().split(/\s+/);
    const hints = [
      input.name, input.id, input.placeholder, input.getAttribute("aria-label"),
      input.getAttribute("inputmode"), input.getAttribute("data-testid")
    ].filter(Boolean).join(" ");
    return autocomplete.includes("one-time-code") ||
      /one.?time|otp|totp|auth.?code|verification.?code|security.?code|mfa.?code/i.test(hints);
  };
  const isTextLike = (input) =>
    ["text", "email", "tel", "url", "search", ""].includes((input.type || "text").toLowerCase());
  const passwordField = inputs.find((input) => (input.type || "").toLowerCase() === "password") ||
    inputs.find(isPasswordField);
  const formRoot = passwordField?.form || passwordField?.closest("form") ||
    passwordField?.getRootNode() || document;
  const candidates = collectInputs(formRoot)
    .filter((input) => usable(input) && input !== passwordField &&
      !isPasswordField(input) && !isTotpField(input) && isTextLike(input));
  const usernameField = candidates.find((input) =>
    (input.autocomplete || "").toLowerCase().split(/\s+/).includes("username")) ||
    candidates.filter((input) => passwordField &&
      (input.compareDocumentPosition(passwordField) & Node.DOCUMENT_POSITION_FOLLOWING)).at(-1) ||
    candidates.find((input) => /user|login|email|account/i.test(
      [input.name, input.id, input.placeholder, input.getAttribute("aria-label")].filter(Boolean).join(" "))) ||
    candidates.find((input) => (input.type || "").toLowerCase() === "email") ||
    candidates[0];

  if (action === "read") {
    return {
      found: Boolean(passwordField || usernameField),
      domain: currentDomain,
      username: usernameField?.value || "",
      password: passwordField?.value || ""
    };
  }

  const setValue = (input, value) => {
    input.focus();
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    if (setter) setter.call(input, value);
    else input.value = value;
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
    input.blur();
  };
  const totpFields = inputs.filter((input) => isTotpField(input) &&
    (isTextLike(input) || (input.type || "").toLowerCase() === "number"));

  if (totpFields.length > 0) {
    if (totpCode) {
      const splitCodeFields = totpFields.length > 1 && totpFields.length === totpCode.length &&
        totpFields.every((input) => input.maxLength === 1);
      if (splitCodeFields) {
        totpFields.forEach((input, index) => setValue(input, totpCode[index]));
      } else {
        setValue(totpFields[0], totpCode);
      }
    } else if (!passwordField) {
      return "totp_unavailable";
    }
    if (!passwordField) return "totp_filled";
  }

  if (!passwordField) {
    const usernameCandidates = inputs.filter((input) => !isTotpField(input) && isTextLike(input));
    const usernameOnlyField = usernameCandidates.find((input) =>
      (input.autocomplete || "").toLowerCase().split(/\s+/).includes("username")) ||
      usernameCandidates.find((input) => /\b(user(name)?|login|email|account)\b/i.test(
        [input.name, input.id, input.placeholder, input.getAttribute("aria-label")].filter(Boolean).join(" "))) ||
      usernameCandidates.find((input) => (input.type || "").toLowerCase() === "email") ||
      usernameCandidates.find((input) => (input.type || "").toLowerCase() === "text") ||
      usernameCandidates[0];
    if (!usernameOnlyField || !username) return "no_password";
    setValue(usernameOnlyField, username);
    return "username_only";
  }

  if (usernameField && username) setValue(usernameField, username);
  setValue(passwordField, password);
  if (totpFields.length > 0) return totpCode ? "totp_filled" : "totp_unavailable";
  return usernameField && username ? "filled" : "no_username";
}

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

chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (typeof changeInfo.url === "string") {
    const version = (matchRefreshVersions.get(tabId) || 0) + 1;
    matchRefreshVersions.set(tabId, version);
    clearMatchBadge(tabId);
  }

  if (changeInfo.status === "complete") {
    void refreshMatchCount(tabId);
  }
});

chrome.tabs.onActivated.addListener(({ tabId }) => {
  void refreshMatchCount(tabId);
});

chrome.tabs.onRemoved.addListener((tabId) => {
  matchRefreshVersions.delete(tabId);
});

chrome.runtime.onInstalled.addListener(() => {
  scheduleReconnectAlarm();
  ensureConnectedQuietly();
});

chrome.runtime.onStartup.addListener(() => {
  scheduleReconnectAlarm();
  ensureConnectedQuietly();
  void chrome.tabs.query({ active: true, lastFocusedWindow: true }).then((tabs) => {
    for (const tab of tabs) if (Number.isInteger(tab.id)) void refreshMatchCount(tab.id);
  }).catch(() => {});
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === keepAliveAlarm) ensureConnectedQuietly();
});

// Re-establish the native connection whenever the service worker starts.
ensureConnectedQuietly();
