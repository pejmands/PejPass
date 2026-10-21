const siteName = document.getElementById("site-name");
const siteDetail = document.getElementById("site-detail");
const version = document.getElementById("version");
const connectionStatus = document.getElementById("connection-status");
const connectionDot = document.getElementById("connection-dot");
const vaultStatus = document.getElementById("vault-status");
const credentialsStatus = document.getElementById("credentials-status");
const credentialList = document.getElementById("credential-list");
const nativeHostName = "com.pejpass.browserhost";
const protocolVersion = 1;

version.textContent = chrome.runtime.getManifest().version;

function setConnectionStatus(connected, message) {
  connectionStatus.textContent = message;
  connectionDot.classList.toggle("connected", connected);
  connectionDot.classList.toggle("disconnected", !connected);
}

function sendNativeMessage(message) {
  return new Promise((resolve, reject) => {
    try {
      chrome.runtime.sendNativeMessage(nativeHostName, message, (response) => {
        const error = chrome.runtime.lastError;
        if (error) { reject(new Error(error.message)); return; }
        resolve(response);
      });
    } catch (error) { reject(error); }
  });
}

function isValidBaseResponse(response, type) {
  return response?.type === type && response?.app === "PejPass" &&
    response?.protocolVersion === protocolVersion;
}

function showUnavailable(message) {
  siteName.textContent = "Unavailable page";
  siteDetail.textContent = message;
  credentialsStatus.textContent = "Credential lookup is unavailable on this page.";
  credentialList.replaceChildren();
}

async function showActiveSite() {
  try {
    const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    const tab = tabs[0];
    if (!tab || typeof tab.url !== "string") {
      showUnavailable("The active tab address is not available."); return;
    }

    let url;
    try { url = new URL(tab.url); }
    catch { showUnavailable("The active tab address could not be read."); return; }

    if (url.protocol !== "http:" && url.protocol !== "https:") {
      showUnavailable("Edge does not allow extensions to inspect this page."); return;
    }

    const domain = url.hostname.toLowerCase().replace(/\.$/, "");
    siteName.textContent = domain;
    siteDetail.textContent = "Searching PejPass for accounts matching this site…";
    await lookupCredentials(domain);
  } catch {
    showUnavailable("Could not read the active tab. Try closing and reopening the popup.");
  }
}

async function checkDesktopConnection() {
  try {
    const response = await sendNativeMessage({ type: "ping", protocolVersion });
    if (isValidBaseResponse(response, "pong")) {
      setConnectionStatus(true, "PejPass desktop bridge connected."); return;
    }
    setConnectionStatus(false, "Desktop bridge returned an unexpected response.");
  } catch { setConnectionStatus(false, "Desktop bridge not installed or unavailable."); }
}

async function checkVaultStatus() {
  vaultStatus.textContent = "Checking vault status…";
  try {
    const response = await sendNativeMessage({ type: "getVaultStatus", protocolVersion });
    if (isValidBaseResponse(response, "vaultStatus") &&
        (response.status === "locked" || response.status === "unlocked")) {
      vaultStatus.textContent = response.status === "unlocked" ? "Vault is unlocked." : "Vault is locked.";
      return;
    }
    vaultStatus.textContent = response?.code === "app_unavailable"
      ? "Vault status unavailable. Open PejPass desktop and try again."
      : "Vault status unavailable because the desktop response was invalid.";
  } catch { vaultStatus.textContent = "Vault status unavailable. Open PejPass desktop and try again."; }
}

async function lookupCredentials(domain) {
  credentialList.replaceChildren();
  credentialsStatus.textContent = "Searching…";
  try {
    const response = await sendNativeMessage({ type: "getCredentials", protocolVersion, domain });
    if (response?.type === "error") {
      const messages = {
        vault_locked: "Unlock the PejPass vault to view matching accounts.",
        app_unavailable: "Open PejPass desktop and try again.",
        invalid_domain: "The active site address is not valid for lookup.",
        lookup_unavailable: "PejPass could not complete the lookup. Try again.",
        unsupported_request: "The running PejPass desktop version does not support account lookup. Fully exit PejPass, then launch the build from PR 138.",
        invalid_app_response: "PejPass desktop returned an incompatible account-lookup response. Fully exit PejPass and relaunch the build from PR 138."
      };
      credentialsStatus.textContent = messages[response.code] ||
        "Credential lookup failed because the desktop response was invalid (code: " +
        String(response.code || "missing") + ").";
      return;
    }

    if (!isValidBaseResponse(response, "credentialMatches") || response.domain !== domain ||
        !Array.isArray(response.credentials) || response.credentials.length > 25 ||
        !response.credentials.every(isValidCredential)) {
      credentialsStatus.textContent = "Credential lookup returned an invalid response."; return;
    }

    if (response.credentials.length === 0) {
      credentialsStatus.textContent = "No matching accounts found."; return;
    }

    credentialsStatus.textContent = response.credentials.length === 25
      ? "Matching accounts (first 25 shown):"
      : response.credentials.length + " matching account" + (response.credentials.length === 1 ? "" : "s") + ":";

    for (const credential of response.credentials) {
      const item = document.createElement("li");
      item.className = "credential-item";
      const title = document.createElement("p");
      title.className = "credential-title";
      title.textContent = credential.title || "Untitled account";
      const username = document.createElement("p");
      username.className = "credential-username";
      username.textContent = credential.username || "No username saved";
      item.append(title, username);
      credentialList.append(item);
    }
  } catch {
    credentialsStatus.textContent = "Credential lookup unavailable. Open PejPass desktop and try again.";
  }
}

function isValidCredential(credential) {
  return credential !== null && typeof credential === "object" &&
    typeof credential.id === "string" && credential.id.length <= 64 &&
    typeof credential.title === "string" && credential.title.length <= 200 &&
    typeof credential.username === "string" && credential.username.length <= 256 &&
    typeof credential.url === "string" && credential.url.length <= 2048;
}

showActiveSite();
checkDesktopConnection();
checkVaultStatus();
