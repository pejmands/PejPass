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
let activeTabId = null;
let activeDomain = null;

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
  activeTabId = null;
  activeDomain = null;
  siteName.textContent = "Unavailable page";
  siteDetail.textContent = message;
  credentialsStatus.textContent = "Credential lookup is unavailable on this page.";
  credentialList.replaceChildren();
}

async function showActiveSite() {
  try {
    const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    const tab = tabs[0];
    if (!tab || typeof tab.url !== "string" || !Number.isInteger(tab.id)) {
      showUnavailable("The active tab address is not available."); return;
    }

    let url;
    try { url = new URL(tab.url); }
    catch { showUnavailable("The active tab address could not be read."); return; }

    if (url.protocol !== "http:" && url.protocol !== "https:") {
      showUnavailable("Edge does not allow extensions to inspect this page."); return;
    }

    activeTabId = tab.id;
    activeDomain = url.hostname.toLowerCase().replace(/\.$/, "");
    siteName.textContent = activeDomain;
    siteDetail.textContent = "Searching PejPass for accounts matching this site…";
    await lookupCredentials(activeDomain);
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
        unsupported_request: "The running PejPass desktop version does not support account lookup. Fully exit PejPass, then launch the latest build.",
        invalid_app_response: "PejPass desktop returned an incompatible account-lookup response. Fully exit PejPass and relaunch the latest build."
      };
      credentialsStatus.textContent = messages[response.code] || "Credential lookup failed because the desktop response was invalid.";
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
      const fillButton = document.createElement("button");
      fillButton.className = "fill-button";
      fillButton.type = "button";
      fillButton.textContent = "Fill login";
      fillButton.addEventListener("click", () => fillCredential(credential));
      item.append(title, username, fillButton);
      credentialList.append(item);
    }
  } catch {
    credentialsStatus.textContent = "Credential lookup unavailable. Open PejPass desktop and try again.";
  }
}

async function fillCredential(credential) {
  if (!activeTabId || !activeDomain || !isValidCredential(credential)) {
    credentialsStatus.textContent = "The active page is no longer available. Reopen the popup.";
    return;
  }

  credentialsStatus.textContent = "Retrieving credentials from PejPass…";
  try {
    const response = await sendNativeMessage({
      type: "fillCredential",
      protocolVersion,
      domain: activeDomain,
      id: credential.id
    });

    if (response?.type === "error") {
      const messages = {
        vault_locked: "Unlock the PejPass vault before filling a login.",
        app_unavailable: "Open PejPass desktop and try again.",
        credential_not_found: "That account is no longer available for this site. Reopen the popup.",
        invalid_domain: "The active site address is not valid for filling.",
        invalid_request: "The fill request was invalid.",
        lookup_unavailable: "PejPass could not retrieve this account. Try again.",
        unsupported_request: "The installed PejPass browser host is outdated. Rebuild the BrowserHost and rerun install-dev-host.ps1.",
        invalid_app_response: "The browser host received an incompatible response from PejPass. Rebuild both PejPass and BrowserHost, then reinstall the host.",
        invalid_message: "The browser host rejected the fill message. Rebuild BrowserHost and reinstall the host."
      };
      credentialsStatus.textContent = messages[response.code] ||
        `PejPass could not retrieve this account (error: ${String(response.code || "unknown")}).`;
      return;
    }

    if (!isValidBaseResponse(response, "credentialForFill") ||
        response.domain !== activeDomain || response.id !== credential.id ||
        typeof response.username !== "string" || response.username.length > 256 ||
        typeof response.password !== "string" || response.password.length > 4096 ||
        (response.totpCode != null && (typeof response.totpCode !== "string" ||
          response.totpCode.length > 8 || (response.totpCode.length > 0 && !/^\\d+$/.test(response.totpCode))))) {
      credentialsStatus.textContent = "PejPass returned an invalid fill response.";
      return;
    }

    const results = await chrome.scripting.executeScript({
      target: { tabId: activeTabId, allFrames: true },
      func: fillLoginForm,
      args: [activeDomain, response.username, response.password, response.totpCode || ""]
    });
    const outcomes = (results || []).map((entry) => entry.result);
    const messages = {
      filled: "Login fields filled. Review them and submit the form yourself.",
      username_only: "Username filled. Continue on the website, then select Fill login again.",
      totp_filled: "Login or verification fields filled. Review them and submit the form yourself.",
      totp_unavailable: "Login fields filled, but PejPass could not generate the TOTP code. Enter it manually.",
      no_password: "No visible password, username, or verification-code field was found in the page or its same-site frames.",
      wrong_page: "The active page changed. Reopen the popup and try again.",
      no_username: "Password filled, but no matching username field was found."
    };
    const outcome = outcomes.includes("totp_filled") ? "totp_filled" :
      outcomes.includes("totp_unavailable") ? "totp_unavailable" :
      outcomes.includes("filled") ? "filled" :
      outcomes.includes("username_only") ? "username_only" :
      outcomes.includes("no_username") ? "no_username" :
      outcomes.includes("wrong_page") ? "wrong_page" : "no_password";
    credentialsStatus.textContent = messages[outcome];
  } catch {
    credentialsStatus.textContent = "Could not fill this page. Check that it is a regular HTTP(S) page and try again.";
  }
}

function fillLoginForm(expectedDomain, username, password, totpCode) {
  const normalizeHost = (host) => host.toLowerCase().replace(/\.$/, "");
  const currentDomain = normalizeHost(window.location.hostname);
  let topDomain;
  try {
    topDomain = normalizeHost(window.top.location.hostname);
  } catch {
    return "wrong_page";
  }
  if (topDomain !== expectedDomain) return "wrong_page";
  if (currentDomain !== expectedDomain && !currentDomain.endsWith("." + expectedDomain)) {
    return "wrong_page";
  }

  const isVisible = (element) => {
    const style = window.getComputedStyle(element);
    return style.display !== "none" && style.visibility !== "hidden" &&
      element.getClientRects().length > 0;
  };
  const usable = (element) => !element.disabled && !element.readOnly && isVisible(element);
  const setValue = (input, value) => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    if (setter) setter.call(input, value);
    else input.value = value;
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  };

  const inputs = Array.from(document.querySelectorAll("input")).filter(usable);
  const isTotpField = (input) => {
    const autocomplete = (input.autocomplete || "").toLowerCase().split(/\\s+/);
    const hints = [input.name, input.id, input.placeholder, input.getAttribute("aria-label"),
      input.getAttribute("inputmode"), input.getAttribute("data-testid")].join(" ");
    return autocomplete.includes("one-time-code") ||
      /one.?time|otp|totp|auth.?code|verification.?code|security.?code|mfa.?code/i.test(hints);
  };
  const isTextLike = (input) =>
    ["text", "email", "tel", "url", "search", ""].includes((input.type || "text").toLowerCase());
  const totpFields = inputs.filter((input) => isTotpField(input) && isTextLike(input));
  const passwordField = inputs.find((input) => (input.type || "").toLowerCase() === "password");

  if (totpFields.length > 0) {
    if (totpCode) {
      setValue(totpFields[0], totpCode);
      totpFields[0].focus();
    } else if (!passwordField) {
      return "totp_unavailable";
    }
    if (!passwordField) return "totp_filled";
  }

  if (!passwordField) {
    const usernameCandidates = inputs.filter((input) => !isTotpField(input) && isTextLike(input));
    const usernameField = usernameCandidates.find((input) =>
      (input.autocomplete || "").toLowerCase().split(/\\s+/).includes("username")) ||
      usernameCandidates.find((input) => /user|login|email|account/i.test(
        [input.name, input.id, input.placeholder, input.getAttribute("aria-label")].join(" "))) ||
      usernameCandidates.find((input) => (input.type || "").toLowerCase() === "email") ||
      usernameCandidates.find((input) => (input.type || "").toLowerCase() === "text");
    if (!usernameField || !username) return "no_password";
    setValue(usernameField, username);
    usernameField.focus();
    return "username_only";
  }

  const form = passwordField.form || passwordField.closest("form") || document;
  const candidates = Array.from(form.querySelectorAll("input"))
    .filter((input) => usable(input) && input !== passwordField && !isTotpField(input) &&
      ["text", "email", "tel", "url"].includes((input.type || "text").toLowerCase()));

  const usernameField = candidates.find((input) =>
    (input.autocomplete || "").toLowerCase().split(/\\s+/).includes("username")) ||
    candidates.filter((input) => {
      return input.compareDocumentPosition(passwordField) & Node.DOCUMENT_POSITION_FOLLOWING;
    }).at(-1) ||
    candidates[0];

  if (usernameField && username) setValue(usernameField, username);
  setValue(passwordField, password);
  passwordField.focus();
  if (totpFields.length > 0) return totpCode ? "totp_filled" : "totp_unavailable";
  return usernameField && username ? "filled" : "no_username";
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
