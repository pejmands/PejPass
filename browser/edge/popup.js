const siteName = document.getElementById("site-name");
const siteDetail = document.getElementById("site-detail");
const version = document.getElementById("version");
const connectionStatus = document.getElementById("connection-status");
const connectionDot = document.getElementById("connection-dot");
const vaultStatus = document.getElementById("vault-status");
const credentialsStatus = document.getElementById("credentials-status");
const credentialList = document.getElementById("credential-list");
const refreshLoginsButton = document.getElementById("refresh-logins");
const inspectLoginButton = document.getElementById("inspect-login");
const saveFields = document.getElementById("save-fields");
const saveTitle = document.getElementById("save-title");
const saveUsername = document.getElementById("save-username");
const savePassword = document.getElementById("save-password");
const saveMode = document.getElementById("save-mode");
const saveTarget = document.getElementById("save-target");
const saveLoginButton = document.getElementById("save-login");
const saveStatus = document.getElementById("save-status");
const saveReviewNotice = document.getElementById("save-review-notice");
const saveConfirmationRow = document.getElementById("save-confirmation-row");
const saveConfirmation = document.getElementById("save-confirmation");
let matchingCredentials = [];
const unlockControls = document.getElementById("unlock-controls");
const unlockPassword = document.getElementById("unlock-password");
const unlockButton = document.getElementById("unlock-vault");
const unlockStatus = document.getElementById("unlock-status");
const launchAppButton = document.getElementById("launch-app");
const lockVaultButton = document.getElementById("lock-vault");
const passwordLength = document.getElementById("password-length");
const generatedPassword = document.getElementById("generated-password");
const generatePasswordButton = document.getElementById("generate-password");
const copyPasswordButton = document.getElementById("copy-password");
const generatorStatus = document.getElementById("generator-status");
const characterOptions = [
  { element: document.getElementById("include-lowercase"), characters: "abcdefghijklmnopqrstuvwxyz" },
  { element: document.getElementById("include-uppercase"), characters: "ABCDEFGHIJKLMNOPQRSTUVWXYZ" },
  { element: document.getElementById("include-numbers"), characters: "0123456789" },
  { element: document.getElementById("include-symbols"), characters: "!@#$%^&*()-_=+[]{};:,.?/" }
];
const viewPanels = {
  logins: document.getElementById("panel-logins"),
  save: document.getElementById("panel-save"),
  generator: document.getElementById("panel-generator")
};
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
  return chrome.runtime
    .sendMessage({ target: "pejpass-native", payload: message })
    .then((response) => {
      if (!response || (response.type === "error" && response.code === "host_unavailable")) {
        throw new Error(response?.message || "Native host unavailable.");
      }
      return response;
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
  matchingCredentials = [];
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
    inspectLoginButton.disabled = false;
    saveLoginButton.disabled = false;
    siteName.textContent = activeDomain;
    siteDetail.textContent = "Searching PejPass for accounts matching this site…";
    await lookupCredentials(activeDomain);
  } catch {
    showUnavailable("Could not read the active tab. Try closing and reopening the popup.");
  }
}

async function refreshLoginView() {
  refreshLoginsButton.disabled = true;
  refreshLoginsButton.textContent = "Refreshing…";
  try {
    await Promise.all([
      showActiveSite(),
      checkDesktopConnection(),
      checkVaultStatus()
    ]);
  } finally {
    refreshLoginsButton.disabled = false;
    refreshLoginsButton.textContent = "Refresh status & accounts";
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

async function checkVaultStatus({ quiet = false } = {}) {
  if (!quiet) vaultStatus.textContent = "Checking vault status…";
  try {
    let response;
    try {
      const cached = await chrome.runtime.sendMessage({ target: "pejpass-state" });
      if (cached?.status === "locked" || cached?.status === "unlocked") {
        response = { type: "vaultStatus", app: "PejPass", protocolVersion, status: cached.status };
      } else if (cached?.status === "unavailable") {
        response = { type: "error", code: "app_unavailable" };
      }
    } catch {
      // Fall back to the native host if the background state is unavailable.
    }

    if (!response) {
      response = await sendNativeMessage({ type: "getVaultStatus", protocolVersion });
    }

    if (isValidBaseResponse(response, "vaultStatus") &&
        (response.status === "locked" || response.status === "unlocked")) {
      vaultStatus.textContent = response.status === "unlocked" ? "Vault is unlocked." : "Vault is locked.";
      unlockControls.hidden = response.status !== "locked";
      launchAppButton.hidden = true;
      lockVaultButton.hidden = response.status !== "unlocked";

      if (response.status === "locked") {
        credentialList.replaceChildren();
        matchingCredentials = [];
        credentialsStatus.textContent = "Unlock the PejPass vault to view matching accounts.";
        saveTitle.value = "";
        saveUsername.value = "";
        savePassword.value = "";
        saveFields.hidden = true;
        saveConfirmation.checked = false;
        generatedPassword.value = "";
        copyPasswordButton.disabled = true;
        generatorStatus.textContent = "Vault locked. Generate a new password when needed.";
      }

      return response.status;
    }
    if (!quiet) {
      const desktopUnavailable = response?.code === "app_unavailable";
      vaultStatus.textContent = desktopUnavailable
        ? "PejPass desktop is not running."
        : "Vault status unavailable because the desktop response was invalid.";
      unlockControls.hidden = !desktopUnavailable;
      launchAppButton.hidden = !desktopUnavailable;
      lockVaultButton.hidden = true;
    }
    return response?.code === "app_unavailable" ? "unavailable" : "unknown";
  } catch {
    if (!quiet) {
      vaultStatus.textContent = "Vault status unavailable. Check that the browser host is installed.";
      unlockControls.hidden = false;
      launchAppButton.hidden = false;
      lockVaultButton.hidden = true;
    }
    return "unavailable";
  }
}

async function lookupCredentials(domain) {
  credentialList.replaceChildren();
  matchingCredentials = [];
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

    matchingCredentials = response.credentials;
    refreshSaveTargets();
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
    const response = await chrome.runtime.sendMessage({
      target: "pejpass-fill",
      tabId: activeTabId,
      domain: activeDomain,
      id: credential.id
    });

    if (response?.type === "error") {
      const messages = {
        vault_locked: "Unlock the PejPass vault before filling a login.",
        app_unavailable: "Open PejPass desktop and try again.",
        credential_not_found: "That account is no longer available for this site. Reopen the popup.",
        fill_denied: "Filling was cancelled in PejPass desktop.",
        fill_unavailable: "PejPass approved the request, but the active page could not be filled. Reopen the popup and try again.",
        invalid_domain: "The active site address is not valid for filling.",
        invalid_request: "The fill request was invalid.",
        lookup_unavailable: "PejPass could not retrieve this account. Try again.",
        unsupported_request: "The installed PejPass browser host is outdated. Rebuild the BrowserHost and rerun install-dev-host.ps1.",
        invalid_app_response: "The browser host received an incompatible response from PejPass. Rebuild both PejPass and BrowserHost, then reinstall the host.",
        invalid_message: "The browser host rejected the fill message. Rebuild BrowserHost and reinstall the host.",
        host_unavailable: "The PejPass browser host is unavailable. Open PejPass desktop and try again."
      };
      credentialsStatus.textContent = messages[response.code] ||
        `PejPass could not retrieve this account (error: ${String(response.code || "unknown")}).`;
      return;
    }

    if (response?.type !== "fillCompleted" || response.app !== "PejPass" ||
        response.protocolVersion !== protocolVersion || response.domain !== activeDomain ||
        response.id !== credential.id ||
        !["filled", "username_only", "totp_filled", "totp_unavailable", "no_password",
          "wrong_page", "no_username"].includes(response.outcome)) {
      credentialsStatus.textContent = "PejPass returned an invalid fill response.";
      return;
    }

    const messages = {
      filled: "Login fields filled. Review them and submit the form yourself.",
      username_only: "Username filled. Continue on the website, then select Fill login again.",
      totp_filled: "Login or verification fields filled. Review them and submit the form yourself.",
      totp_unavailable: "Login fields filled, but PejPass could not generate the TOTP code. Enter it manually.",
      no_password: "No visible password, username, or verification-code field was found in the page or its same-site frames.",
      wrong_page: "The active page changed. Reopen the popup and try again.",
      no_username: "Password filled, but no matching username field was found."
    };
    credentialsStatus.textContent = messages[response.outcome];
  } catch {
    credentialsStatus.textContent = "Could not fill this page. Check that it is a regular HTTP(S) page and try again.";
  }
}

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
      func: processLoginForm,
      args: ["read", domain]
    });
    const candidates = (results || []).map((entry) => entry.result)
      .filter((result) => result && result.found && result.domain === domain);
    const candidate = candidates.find((result) => result.password) ||
      candidates.find((result) => result.username);
    if (!candidate) {
      saveStatus.textContent = "No visible username or password field was found. Fill the form first, then try again.";
      return;
    }

    saveTitle.value = siteName.textContent || domain;
    saveUsername.value = candidate.username || "";
    savePassword.value = candidate.password || "";
    saveFields.hidden = false;
    saveConfirmation.checked = false;
    saveStatus.textContent = "Review or correct these values. They are not saved until you confirm.";
    saveMode.value = "create";
    saveTarget.value = "";
    saveTarget.hidden = true;
    savePassword.type = "password";
    updateSaveReview();
  } catch {
    saveStatus.textContent = "Could not inspect this page. Check that it is a regular HTTP(S) page.";
  } finally {
    inspectLoginButton.disabled = false;
  }
}


function refreshSaveTargets() {
  const selectedId = saveTarget.value;
  saveTarget.replaceChildren(new Option("Choose an account to update", ""));
  for (const credential of matchingCredentials) {
    const label = (credential.title || "Untitled account") +
      (credential.username ? " — " + credential.username : " — blank username");
    saveTarget.add(new Option(label, credential.id));
  }
  if ([...saveTarget.options].some((option) => option.value === selectedId))
    saveTarget.value = selectedId;
}

function updateSaveReview() {
  if (saveFields.hidden) {
    saveLoginButton.disabled = false;
    return;
  }

  const title = saveTitle.value.trim();
  const username = saveUsername.value.trim();
  const password = savePassword.value;
  const mode = saveMode.value;
  const target = matchingCredentials.find((credential) => credential.id === saveTarget.value);
  const matches = matchingCredentials.filter((credential) =>
    credential.username.trim().toLowerCase() === username.toLowerCase());

  if (!title || !password || password.length > 4096 ||
      username.length > 256 || title.length > 200) {
    saveReviewNotice.textContent = !title
      ? "Enter an account name before saving."
      : !password
        ? "A password is required before this login can be saved."
        : "Check the field lengths before saving.";
  } else if (mode === "update" && !target) {
    saveReviewNotice.textContent = "Choose the exact saved account you want to update. Its previous values will be retained in history.";
  } else if (mode === "update") {
    saveReviewNotice.textContent = "You selected “" + (target.title || "Untitled account") +
      "” for update. Review carefully; this exact entry will be changed and previous values retained in history.";
  } else if (matches.length > 0) {
    saveReviewNotice.textContent = matches.length === 1
      ? "A saved account has the same username, but this action will create a separate new entry."
      : "Several saved accounts use this username. This action will create a separate new entry; no existing account will be changed.";
  } else if (!username) {
    saveReviewNotice.textContent = "The username is blank. Save only if this login legitimately has no username.";
  } else {
    saveReviewNotice.textContent = "This action creates a new entry. Existing accounts will not be changed.";
  }

  const invalid = !title || !password || password.length > 4096 ||
    username.length > 256 || title.length > 200 ||
    (mode === "update" && !target);
  saveLoginButton.disabled = invalid || !saveConfirmation.checked;
}

async function saveLogin() {
  updateSaveReview();
  if (!activeTabId || !activeDomain || saveFields.hidden) {
    saveStatus.textContent = "Read and review the login form first.";
    return;
  }
  if (!saveTitle.value.trim() || !savePassword.value || savePassword.value.length > 4096 ||
      saveUsername.value.length > 256 || saveTitle.value.trim().length > 200 ||
      (saveMode.value === "update" && !saveTarget.value) ||
      !["create", "update"].includes(saveMode.value) ||
      saveConfirmation.checked !== true) {
    saveStatus.textContent = "Complete the required fields, review the warning, and confirm before saving.";
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
      url: new URL(url.href).toString().split(/[?#]/, 1)[0],
      title: saveTitle.value.trim(),
      username: saveUsername.value.trim(),
      password: savePassword.value,
      mode: saveMode.value,
      ...(saveMode.value === "update" ? { entryId: saveTarget.value } : {})
    });
    if (response?.type === "error") {
      const messages = {
        vault_locked: "Unlock the PejPass vault before saving a login.",
        app_unavailable: "Open the latest PejPass desktop build and try again.",
        invalid_domain: "The active site address is invalid.",
        invalid_request: "Check the login fields and try again.",
        vault_changed: "The vault changed on disk. Reopen it in PejPass before saving.",
        credential_not_found: "That account is no longer available for this site. Refresh accounts and choose it again.",
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

    saveTitle.value = "";
    saveUsername.value = "";
    savePassword.value = "";
    saveMode.value = "create";
    saveTarget.value = "";
    saveTarget.hidden = true;
    saveConfirmation.checked = false;
    saveFields.hidden = true;

    await lookupCredentials(activeDomain);
  } catch {
    saveStatus.textContent = "Could not save the login. Check that PejPass desktop and BrowserHost are running.";
  } finally {
    updateSaveReview();
  }
}

for (const field of [saveTitle, saveUsername, savePassword]) {
  field.addEventListener("input", () => {
    saveConfirmation.checked = false;
    updateSaveReview();
  });
}
saveMode.addEventListener("change", () => {
  saveConfirmation.checked = false;
  saveTarget.hidden = saveMode.value !== "update";
  if (saveMode.value === "update") refreshSaveTargets();
  updateSaveReview();
});
saveTarget.addEventListener("change", () => {
  saveConfirmation.checked = false;
  updateSaveReview();
});
saveConfirmation.addEventListener("change", updateSaveReview);

inspectLoginButton.addEventListener("click", inspectLoginForm);
saveLoginButton.addEventListener("click", saveLogin);
refreshLoginsButton.addEventListener("click", refreshLoginView);

function isValidCredential(credential) {
  return credential !== null && typeof credential === "object" &&
    typeof credential.id === "string" && credential.id.length <= 64 &&
    typeof credential.title === "string" && credential.title.length <= 200 &&
    typeof credential.username === "string" && credential.username.length <= 256 &&
    typeof credential.url === "string" && credential.url.length <= 2048;
}


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
    .filter((option) => option.element.checked)
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

async function launchDesktopApp() {
  launchAppButton.disabled = true;
  launchAppButton.textContent = "Starting PejPass…";
  vaultStatus.textContent = "Starting PejPass desktop…";
  try {
    const response = await sendNativeMessage({ type: "launchApp", protocolVersion });
    if (!isValidBaseResponse(response, "appLaunched")) {
      vaultStatus.textContent = response?.code === "app_unavailable"
        ? "Could not locate PejPass desktop. Build the desktop app in Release and reinstall the browser host."
        : "Could not start PejPass desktop.";
      return;
    }

    for (let attempt = 0; attempt < 20; attempt++) {
      await new Promise((resolve) => setTimeout(resolve, 500));
      const status = await checkVaultStatus();

      if (status === "locked" || status === "unlocked") return;
    }
    vaultStatus.textContent = "PejPass is starting. Try checking the vault status again in a moment.";
  } catch {
    vaultStatus.textContent = "Could not start PejPass. Check that the browser host is installed.";
  } finally {
    launchAppButton.disabled = false;
    launchAppButton.textContent = "Launch PejPass";
  }
}

async function lockVault() {
  if (lockVaultButton.disabled)
    return;

  lockVaultButton.disabled = true;
  lockVaultButton.textContent = "Locking vault…";
  vaultStatus.textContent = "Locking the PejPass vault…";
  try {
    const response = await sendNativeMessage({ type: "lockVault", protocolVersion });
    if (!isValidBaseResponse(response, "vaultLocked") || response.status !== "locked") {
      vaultStatus.textContent = response?.code === "app_unavailable"
        ? "Could not lock the vault. Open PejPass desktop and try again."
        : "PejPass returned an invalid vault-lock response.";
      return;
    }

    credentialList.replaceChildren();
    matchingCredentials = [];
    credentialsStatus.textContent = "Unlock the PejPass vault to view matching accounts.";
    saveTitle.value = "";
    saveUsername.value = "";
    savePassword.value = "";
    saveFields.hidden = true;
    saveConfirmation.checked = false;
    saveStatus.textContent = "Vault locked. Read the login form again after unlocking to save.";
    unlockPassword.value = "";
    generatedPassword.value = "";
    copyPasswordButton.disabled = true;
    generatorStatus.textContent = "Vault locked. Generate a new password when needed.";
    vaultStatus.textContent = "Vault is locked.";
    unlockControls.hidden = false;
    launchAppButton.hidden = true;
    lockVaultButton.hidden = true;
    unlockStatus.textContent = "Enter your master password to unlock the vault.";
  } catch {
    vaultStatus.textContent = "Could not connect to PejPass to lock the vault.";
  } finally {
    lockVaultButton.disabled = false;
    lockVaultButton.textContent = "Lock vault";
    updateSaveReview();
  }
}

let unlockCooldownTimer = null;
let unlockCoolingDown = false;

function startUnlockCooldown(seconds) {
  clearInterval(unlockCooldownTimer);
  let remaining = Math.max(1, Math.min(Number(seconds) || 1, 3600));
  unlockCoolingDown = true;

  const tick = () => {
    if (remaining <= 0) {
      clearInterval(unlockCooldownTimer);
      unlockCooldownTimer = null;
      unlockCoolingDown = false;
      unlockButton.disabled = false;
      unlockButton.textContent = "Unlock vault";
      unlockStatus.textContent = "You can try again.";
      return;
    }

    unlockButton.disabled = true;
    unlockButton.textContent = `Try again in ${remaining}s`;
    remaining--;
  };

  tick();
  unlockCooldownTimer = setInterval(tick, 1000);
}

async function unlockVault() {
  if (unlockCoolingDown) return;
  if (!unlockPassword.value || unlockPassword.value.length > 4096) {
    unlockStatus.textContent = "Enter your master password.";
    return;
  }

  unlockButton.disabled = true;
  launchAppButton.disabled = true;
  unlockStatus.textContent = "Verifying master password in PejPass…";
  try {
    const response = await sendNativeMessage({
      type: "unlockVault",
      protocolVersion,
      masterPassword: unlockPassword.value
    });
    if (isValidBaseResponse(response, "vaultUnlocked") && response.status === "unlocked") {
      unlockStatus.textContent = "Vault unlocked.";
      await checkVaultStatus();
      if (activeDomain) await lookupCredentials(activeDomain);
      return;
    }

    if (response?.code === "unlock_rate_limited") {
      startUnlockCooldown(response.retryAfterSeconds);
      unlockStatus.textContent = "Too many attempts. Please wait before trying again.";
      return;
    }

    const messages = {
      unlock_failed: "The master password was not accepted, or no existing vault is selected in PejPass.",
      app_unavailable: "Could not start PejPass. Check the desktop build and browser host installation.",
      invalid_request: "The password could not be sent. Try again.",
      unsupported_request: "The installed browser host or desktop app is outdated. Rebuild and reinstall the latest version."
    };
    unlockStatus.textContent = messages[response?.code] || "Could not unlock the vault. Check the PejPass desktop connection.";
  } catch {
    unlockStatus.textContent = "Could not connect to PejPass. Check the browser host installation.";
  } finally {
    unlockPassword.value = "";
    unlockButton.disabled = unlockCoolingDown;
    if (!unlockCoolingDown) unlockButton.textContent = "Unlock vault";
    launchAppButton.disabled = false;
  }
}

unlockButton.addEventListener("click", unlockVault);
lockVaultButton.addEventListener("click", lockVault);
unlockPassword.addEventListener("keydown", (event) => {
  if (event.key === "Enter") unlockVault();
});
launchAppButton.addEventListener("click", launchDesktopApp);

const viewTabs = Array.from(document.querySelectorAll(".view-tab"));

function setActiveView(view, moveFocus = false) {
  if (!Object.hasOwn(viewPanels, view)) return;

  for (const tab of viewTabs) {
    const selected = tab.dataset.view === view;
    tab.setAttribute("aria-selected", String(selected));
    tab.tabIndex = selected ? 0 : -1;
    if (selected && moveFocus) tab.focus();
  }

  for (const [name, panel] of Object.entries(viewPanels)) {
    panel.hidden = name !== view;
  }
}

viewTabs.forEach((tab, index) => {
  tab.addEventListener("click", () => setActiveView(tab.dataset.view));
  tab.addEventListener("keydown", (event) => {
    let nextIndex = index;
    if (event.key === "ArrowRight") nextIndex = (index + 1) % viewTabs.length;
    else if (event.key === "ArrowLeft") nextIndex = (index - 1 + viewTabs.length) % viewTabs.length;
    else if (event.key === "Home") nextIndex = 0;
    else if (event.key === "End") nextIndex = viewTabs.length - 1;
    else return;

    event.preventDefault();
    setActiveView(viewTabs[nextIndex].dataset.view, true);
  });
});

setActiveView("logins");
showActiveSite();
checkDesktopConnection();
checkVaultStatus();

chrome.runtime.onMessage.addListener((message) => {
  if (message?.type !== "vaultStateChanged") return;

  void checkVaultStatus().then((status) => {
    if (status === "unlocked" && activeDomain && matchingCredentials.length === 0) {
      return lookupCredentials(activeDomain);
    }
  });
});
