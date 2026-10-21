const siteName = document.getElementById("site-name");
const siteDetail = document.getElementById("site-detail");
const version = document.getElementById("version");
const connectionStatus = document.getElementById("connection-status");
const connectionDot = document.getElementById("connection-dot");
const vaultStatus = document.getElementById("vault-status");
const credentialsStatus = document.getElementById("credentials-status");
const credentialList = document.getElementById("credential-list");
const inspectLoginButton = document.getElementById("inspect-login");
const saveFields = document.getElementById("save-fields");
const saveTitle = document.getElementById("save-title");
const saveUsername = document.getElementById("save-username");
const savePassword = document.getElementById("save-password");
const saveLoginButton = document.getElementById("save-login");
const saveStatus = document.getElementById("save-status");
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
  inspectLoginButton.disabled = true;
  saveLoginButton.disabled = true;
  saveFields.hidden = true;
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
          response.totpCode.length > 8 || (response.totpCode.length > 0 && !/^\d+$/.test(response.totpCode))))) {
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
    const autocomplete = (input.autocomplete || "").toLowerCase().split(/\s+/);
    const hints = [input.name, input.id, input.placeholder, input.getAttribute("aria-label"),
      input.getAttribute("inputmode"), input.getAttribute("data-testid")].join(" ");
    return autocomplete.includes("one-time-code") ||
      /one.?time|otp|totp|auth.?code|verification.?code|security.?code|mfa.?code/i.test(hints);
  };
  const isTextLike = (input) =>
    ["text", "email", "tel", "url", "search", ""].includes((input.type || "text").toLowerCase());
  const totpFields = inputs.filter((input) => isTotpField(input) &&
    (isTextLike(input) || (input.type || "").toLowerCase() === "number"));
  const passwordField = inputs.find((input) => (input.type || "").toLowerCase() === "password");

  if (totpFields.length > 0) {
    if (totpCode) {
      const splitCodeFields = totpFields.length > 1 && totpFields.length === totpCode.length &&
        totpFields.every((input) => input.maxLength === 1);
      if (splitCodeFields) {
        totpFields.forEach((input, index) => setValue(input, totpCode[index]));
      } else {
        setValue(totpFields[0], totpCode);
      }
      totpFields[0].focus();
    } else if (!passwordField) {
      return "totp_unavailable";
    }
    if (!passwordField) return "totp_filled";
  }

  if (!passwordField) {
    const usernameCandidates = inputs.filter((input) => !isTotpField(input) && isTextLike(input));
    const usernameField = usernameCandidates.find((input) =>
      (input.autocomplete || "").toLowerCase().split(/\s+/).includes("username")) ||
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
    (input.autocomplete || "").toLowerCase().split(/\s+/).includes("username")) ||
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

async function inspectLoginForm() {
  if (!activeTabId || !activeDomain) {
    saveStatus.textContent = "Open a regular HTTP(S) login page first.";
    return;
  }

  inspectLoginButton.disabled = true;
  saveStatus.textContent = "Reading visible login fields…";
  saveFields.hidden = true;
  try {
    const tabId = activeTabId;
    const domain = activeDomain;
    const results = await chrome.scripting.executeScript({
      target: { tabId, allFrames: true },
      func: readLoginForm,
      args: [domain]
    });
    const candidates = (results || []).map((entry) => entry.result)
      .filter((result) => result && result.found && result.domain === domain);
    const candidate = candidates.find((result) => result.password) ||
      candidates.find((result) => result.username);
    if (!candidate) {
      saveStatus.textContent = "No visible username or password field was found. Fill the form first, then try again.";
      return;
    }

    saveTitle.value = document.getElementById("site-name").textContent || domain;
    saveUsername.value = candidate.username || "";
    savePassword.value = candidate.password || "";
    saveFields.hidden = false;
    saveStatus.textContent = "Review or correct these values. They are not saved until you confirm.";
    savePassword.type = "password";
  } catch {
    saveStatus.textContent = "Could not inspect this page. Check that it is a regular HTTP(S) page.";
  } finally {
    inspectLoginButton.disabled = false;
  }
}

function readLoginForm(expectedDomain) {
  const normalizeHost = (host) => host.toLowerCase().replace(/\.$/, "");
  const currentDomain = normalizeHost(window.location.hostname);
  let topDomain;
  try { topDomain = normalizeHost(window.top.location.hostname); }
  catch { return { found: false }; }
  if (topDomain !== expectedDomain ||
      (currentDomain !== expectedDomain && !currentDomain.endsWith("." + expectedDomain))) {
    return { found: false };
  }

  const isVisible = (element) => {
    const style = window.getComputedStyle(element);
    return style.display !== "none" && style.visibility !== "hidden" &&
      element.getClientRects().length > 0;
  };
  const inputs = Array.from(document.querySelectorAll("input"))
    .filter((input) => !input.disabled && !input.readOnly && isVisible(input));
  const passwordField = inputs.find((input) => (input.type || "").toLowerCase() === "password");
  const form = passwordField?.form || passwordField?.closest("form") || document;
  const candidates = Array.from(form.querySelectorAll("input")).filter((input) =>
    !input.disabled && !input.readOnly && isVisible(input) && input !== passwordField &&
    ["text", "email", "tel"].includes((input.type || "text").toLowerCase()));
  const usernameField = candidates.find((input) =>
    (input.autocomplete || "").toLowerCase().split(/\s+/).includes("username")) ||
    candidates.filter((input) => passwordField &&
      (input.compareDocumentPosition(passwordField) & Node.DOCUMENT_POSITION_FOLLOWING)).at(-1) ||
    candidates.find((input) => (input.type || "").toLowerCase() === "email") ||
    candidates[0];

  return {
    found: Boolean(passwordField || usernameField),
    domain: currentDomain,
    username: usernameField?.value || "",
    password: passwordField?.value || ""
  };
}

async function saveLogin() {
  if (!activeTabId || !activeDomain || saveFields.hidden) {
    saveStatus.textContent = "Read and review the login form first.";
    return;
  }
  if (!savePassword.value || savePassword.value.length > 4096 ||
      saveUsername.value.length > 256 || saveTitle.value.length > 200) {
    saveStatus.textContent = "Enter a password (maximum 4096 characters) and check the field lengths.";
    return;
  }

  saveLoginButton.disabled = true;
  saveStatus.textContent = "Saving to the unlocked PejPass vault…";
  try {
    const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    const url = new URL(tabs[0]?.url || "");
    const domain = url.hostname.toLowerCase().replace(/\.$/, "");
    if (tabs[0]?.id !== activeTabId || domain !== activeDomain ||
        !["http:", "https:"].includes(url.protocol)) {
      saveStatus.textContent = "The active page changed. Reopen the popup and inspect the form again.";
      return;
    }

    const response = await sendNativeMessage({
      type: "saveCredential",
      protocolVersion,
      domain: activeDomain,
      url: url.href,
      title: saveTitle.value.trim(),
      username: saveUsername.value.trim(),
      password: savePassword.value
    });
    if (response?.type === "error") {
      const messages = {
        vault_locked: "Unlock the PejPass vault before saving a login.",
        app_unavailable: "Open the latest PejPass desktop build and try again.",
        invalid_domain: "The active site address is invalid.",
        invalid_request: "Check the login fields and try again.",
        vault_changed: "The vault changed on disk. Reopen it in PejPass before saving.",
        multiple_matching_accounts: "More than one account matches this username and site. Update the account manually in PejPass.",
        save_unavailable: "PejPass could not save the login. Check that the vault is writable and try again.",
        unsupported_request: "The running PejPass desktop or BrowserHost is outdated. Rebuild and reinstall the latest version.",
        invalid_app_response: "PejPass returned an incompatible save response. Rebuild both desktop and BrowserHost."
      };
      saveStatus.textContent = messages[response.code] || "Could not save the login.";
      return;
    }

    if (!isValidBaseResponse(response, "credentialSaved") ||
        response.domain !== activeDomain || typeof response.id !== "string" ||
        !["created", "updated", "unchanged"].includes(response.result)) {
      saveStatus.textContent = "PejPass returned an invalid save response.";
      return;
    }

    saveStatus.textContent = response.result === "created" ? "New login saved to PejPass." :
      response.result === "updated" ? "Existing login updated. Previous values were added to history." :
      "This login already contains the same password and URL; nothing changed.";
    await lookupCredentials(activeDomain);
  } catch {
    saveStatus.textContent = "Could not save the login. Check that PejPass desktop and BrowserHost are running.";
  } finally {
    saveLoginButton.disabled = false;
  }
}

inspectLoginButton.addEventListener("click", inspectLoginForm);
saveLoginButton.addEventListener("click", saveLogin);

function isValidCredential(credential) {
  return credential !== null && typeof credential === "object" &&
    typeof credential.id === "string" && credential.id.length <= 64 &&
    typeof credential.title === "string" && credential.title.length <= 200 &&
    typeof credential.username === "string" && credential.username.length <= 256 &&
    typeof credential.url === "string" && credential.url.length <= 2048;
}


const passwordLength = document.getElementById("password-length");
const generatedPassword = document.getElementById("generated-password");
const generatePasswordButton = document.getElementById("generate-password");
const copyPasswordButton = document.getElementById("copy-password");
const generatorStatus = document.getElementById("generator-status");
const characterOptions = [
  { id: "include-lowercase", characters: "abcdefghijklmnopqrstuvwxyz" },
  { id: "include-uppercase", characters: "ABCDEFGHIJKLMNOPQRSTUVWXYZ" },
  { id: "include-numbers", characters: "0123456789" },
  { id: "include-symbols", characters: "!@#$%^&*()-_=+[]{};:,.?/" }
];

function secureRandomIndex(maximum) {
  if (!Number.isInteger(maximum) || maximum < 1 || maximum > 0x100000000) {
    throw new RangeError("Invalid random range.");
  }

  const range = 0x100000000;
  const limit = Math.floor(range / maximum) * maximum;
  const values = new Uint32Array(1);
  let value;

  do {
    crypto.getRandomValues(values);
    value = values[0];
  } while (value >= limit);

  return value % maximum;
}

function generateSecurePassword() {
  const length = Number(passwordLength.value);
  const selectedSets = characterOptions
    .filter((option) => document.getElementById(option.id).checked)
    .map((option) => option.characters);

  if (!Number.isInteger(length) || length < 8 || length > 64) {
    throw new Error("Choose a password length between 8 and 64.");
  }
  if (selectedSets.length === 0) {
    throw new Error("Select at least one character type.");
  }

  const characters = selectedSets.join("");
  const password = selectedSets.map((set) => set[secureRandomIndex(set.length)]);

  while (password.length < length) {
    password.push(characters[secureRandomIndex(characters.length)]);
  }

  for (let index = password.length - 1; index > 0; index--) {
    const otherIndex = secureRandomIndex(index + 1);
    [password[index], password[otherIndex]] = [password[otherIndex], password[index]];
  }

  return password.join("");
}

generatePasswordButton.addEventListener("click", () => {
  try {
    const password = generateSecurePassword();
    generatedPassword.value = password;
    copyPasswordButton.disabled = false;
    generatorStatus.textContent = "Generated locally in this browser. Not saved automatically.";
  } catch (error) {
    generatedPassword.value = "";
    copyPasswordButton.disabled = true;
    generatorStatus.textContent = error instanceof Error
      ? error.message
      : "Could not generate a password.";
  }
});

copyPasswordButton.addEventListener("click", async () => {
  if (!generatedPassword.value) return;

  try {
    await navigator.clipboard.writeText(generatedPassword.value);
    generatorStatus.textContent = "Password copied to the clipboard.";
  } catch {
    generatorStatus.textContent = "Copy failed. Select and copy the password manually.";
  }
});

showActiveSite();
checkDesktopConnection();
checkVaultStatus();
