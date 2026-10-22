const siteName = document.getElementById("site-name");
const siteDetail = document.getElementById("site-detail");
const version = document.getElementById("version");
const connectionStatus = document.getElementById("connection-status");
const connectionDot = document.getElementById("connection-dot");
const vaultStatus = document.getElementById("vault-status");
const credentialsStatus = document.getElementById("credentials-status");
const credentialList = document.getElementById("credential-list");
const fillNowButton = document.getElementById("fill-now");
const refreshLoginsButton = document.getElementById("refresh-logins");
const inspectLoginButton = document.getElementById("inspect-login");
const saveFields = document.getElementById("save-fields");
const tabSave = document.getElementById("tab-save");
const saveBadge = document.getElementById("save-badge");
const toggleSavePassword = document.getElementById("toggle-save-password");
const saveDiff = document.getElementById("save-diff");
const saveDiffList = document.getElementById("save-diff-list");
let saveTitleEdited = false;
let pendingSaveUrl = "";
let savePasswordTimer = null;
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
const unlockHelloButton = document.getElementById("unlock-hello");
const unlockErrorDetails = document.getElementById("unlock-error-details");
const unlockErrorDetail = document.getElementById("unlock-error-detail");
const statusDetails = document.getElementById("status-details");
const statusDetailText = document.getElementById("status-detail-text");
const launchAppButton = document.getElementById("launch-app");
const lockVaultButton = document.getElementById("lock-vault");
const passwordLength = document.getElementById("password-length");
const generatedPassword = document.getElementById("generated-password");
const generatePasswordButton = document.getElementById("generate-password");
const generateFillPasswordButton = document.getElementById("generate-fill-password");
const copyPasswordButton = document.getElementById("copy-password");
const excludeAmbiguous = document.getElementById("exclude-ambiguous");
const passwordStrength = document.getElementById("password-strength");
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

function showStatusError(message, details) {
  vaultStatus.textContent = message;
  statusDetailText.textContent = details;
  statusDetails.hidden = false;
}

function clearStatusError() {
  statusDetails.hidden = true;
  statusDetails.open = false;
  statusDetailText.textContent = "";
}

function showUnlockError(message, details) {
  unlockStatus.textContent = message;
  unlockErrorDetail.textContent = details;
  unlockErrorDetails.hidden = false;
}

function clearUnlockError() {
  unlockErrorDetails.hidden = true;
  unlockErrorDetails.open = false;
  unlockErrorDetail.textContent = "";
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
  generateFillPasswordButton.disabled = true;
  saveFields.hidden = true;
  saveBadge.hidden = true;
  credentialList.replaceChildren();
  matchingCredentials = [];
  fillNowButton.hidden = true;
}

async function probeSaveCandidate() {
  saveBadge.hidden = true;
  tabSave.removeAttribute("aria-label");
  if (!activeTabId || !activeDomain) return;

  try {
    const results = await chrome.scripting.executeScript({
      target: { tabId: activeTabId, allFrames: true },
      func: processLoginForm,
      args: ["probe", activeDomain]
    });
    const hit = (results || []).some((entry) =>
      entry.result?.found === true &&
      entry.result.domain === activeDomain &&
      entry.result.hasPassword === true);

    saveBadge.hidden = !hit;
    if (hit) tabSave.setAttribute("aria-label", "Save, a password has been entered on this page");
  } catch {
    // Pages that do not allow script injection do not show a suggestion.
  }
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
    generateFillPasswordButton.disabled = false;
    siteName.textContent = activeDomain;
    siteDetail.textContent = "Searching PejPass for accounts matching this site…";
    void probeSaveCandidate();
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
      checkVaultStatus()
    ]);
  } finally {
    refreshLoginsButton.disabled = false;
    refreshLoginsButton.textContent = "Refresh status & accounts";
  }
}

async function checkVaultStatus({ quiet = false } = {}) {
  if (!quiet) {
    vaultStatus.textContent = "Checking vault…";
    clearStatusError();
  }
  try {
    let response;
    try {
      const cached = await chrome.runtime.sendMessage({ target: "pejpass-state" });
      if (cached?.status === "locked" || cached?.status === "unlocked") {
        setConnectionStatus(true, "Desktop connected");
        response = { type: "vaultStatus", app: "PejPass", protocolVersion, status: cached.status };
      } else if (cached?.status === "unavailable") {
        setConnectionStatus(false, "Desktop unavailable");
        response = { type: "error", code: "app_unavailable" };
      }
    } catch {
      // Fall back to the native host if the background state is unavailable.
    }

    if (!response) {
      response = await sendNativeMessage({ type: "getVaultStatus", protocolVersion });
      setConnectionStatus(true, "Desktop connected");
    }

    if (isValidBaseResponse(response, "vaultStatus") &&
        (response.status === "locked" || response.status === "unlocked")) {
      vaultStatus.textContent = response.status === "unlocked" ? "Vault unlocked" : "Vault locked";
      clearStatusError();
      unlockControls.hidden = response.status !== "locked";
      unlockButton.textContent = "Unlock vault";
      unlockHelloButton.hidden = response.status !== "locked";
      launchAppButton.hidden = true;
      lockVaultButton.hidden = response.status !== "unlocked";

      if (response.status === "locked") {
        credentialList.replaceChildren();
        matchingCredentials = [];
        fillNowButton.hidden = true;
        credentialsStatus.textContent = "Unlock the PejPass vault to view matching accounts.";
        saveTitle.value = "";
        saveUsername.value = "";
        savePassword.value = "";
        setSavePasswordVisible(false);
        saveFields.hidden = true;
        saveBadge.hidden = true;
        saveConfirmation.checked = false;
        generatedPassword.value = "";
        copyPasswordButton.disabled = true;
        generatorStatus.textContent = "Vault locked. Generate a new password when needed.";
      }

      return response.status;
    }

    if (!quiet) {
      const desktopUnavailable = response?.code === "app_unavailable";
      unlockControls.hidden = !desktopUnavailable;
      unlockButton.textContent = desktopUnavailable ? "Launch & unlock" : "Unlock vault";
      unlockHelloButton.hidden = true;
      launchAppButton.hidden = true;
      lockVaultButton.hidden = true;

      if (desktopUnavailable) {
        setConnectionStatus(false, "Desktop unavailable");
        showStatusError("PejPass isn’t running.", "The native messaging host could not reach the PejPass desktop pipe. Check the desktop installation and browser host registration.");
      } else {
        setConnectionStatus(false, "Connection error");
        showStatusError("Couldn’t check vault status.", response?.code || "The desktop returned an invalid or incompatible response.");
      }
    }
    return response?.code === "app_unavailable" ? "unavailable" : "unknown";
  } catch (error) {
    setConnectionStatus(false, "Desktop unavailable");
    if (!quiet) {
      unlockControls.hidden = false;
      unlockButton.textContent = "Launch & unlock";
      unlockHelloButton.hidden = true;
      launchAppButton.hidden = true;
      lockVaultButton.hidden = true;
      showStatusError("Couldn’t connect to PejPass.", error instanceof Error ? error.message : "The browser host could not be reached.");
    }
    return "unavailable";
  }
}

async function lookupCredentials(domain) {
  credentialList.replaceChildren();
  matchingCredentials = [];
  updateMatchBadge(0);
  fillNowButton.hidden = true;
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
      fillNowButton.hidden = true;
      credentialsStatus.textContent = "No matching accounts found."; return;
    }

    credentialsStatus.textContent = response.credentials.length === 25
      ? "Matching accounts (first 25 shown):"
      : response.credentials.length + " matching account" + (response.credentials.length === 1 ? "" : "s") + ":";

    const singleLoginMatch = response.credentials.length === 1 &&
      activeTabId !== null && await detectLoginPage(activeTabId, domain);
    fillNowButton.hidden = !singleLoginMatch;
    if (singleLoginMatch) {
      fillNowButton.textContent = "Fill now";
    }

    for (const credential of response.credentials) {
      const item = document.createElement("li");
      item.className = singleLoginMatch ? "credential-item single-match" : "credential-item";
      const title = document.createElement("p");
      title.className = "credential-title";
      title.dir = "auto";
      title.textContent = credential.title || "Untitled account";
      const username = document.createElement("p");
      username.className = "credential-username";
      username.dir = "auto";
      username.textContent = credential.username || "No username saved";
      const fillButton = document.createElement("button");
      fillButton.className = "fill-button";
      fillButton.type = "button";
      fillButton.textContent = "Fill login";
      fillButton.setAttribute("aria-label", `Fill login for ${credential.title || "Untitled account"}`);
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
        hello_failed: "Windows Hello did not verify your identity. No credentials were released.",
        hello_cancelled: "Windows Hello verification was cancelled. No credentials were released.",
        hello_unavailable: "Windows Hello is unavailable or not configured. Set it up in Windows, then try again.",
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

async function detectLoginPage(tabId, domain) {
  try {
    const results = await chrome.scripting.executeScript({
      target: { tabId, allFrames: true },
      func: processLoginForm,
      args: ["detect", domain]
    });
    return (results || []).some((entry) =>
      entry.result?.found === true && entry.result.domain === domain);
  } catch {
    return false;
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

  if (action === "detect") {
    return { found: Boolean(passwordField || usernameField), domain: currentDomain };
  }

  if (action === "probe") {
    // Return only whether a password value exists; never return the value itself.
    return {
      found: Boolean(passwordField || usernameField),
      domain: currentDomain,
      hasPassword: Boolean(passwordField && passwordField.value)
    };
  }

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

    saveTitleEdited = false;
    saveTitle.value = siteName.textContent || domain;
    saveUsername.value = candidate.username || "";
    savePassword.value = candidate.password || "";
    setSavePasswordVisible(false);
    pendingSaveUrl = await currentPageUrl(tabId);

    saveFields.hidden = false;
    saveConfirmation.checked = false;
    saveStatus.textContent = "Review or correct these values, then choose Save or Update.";
    applyDefaultSaveMode(saveUsername.value);
    updateSaveReview();
  } catch {
    saveStatus.textContent = "Could not inspect this page. Check that it is a regular HTTP(S) page.";
  } finally {
    inspectLoginButton.disabled = false;
  }
}


function setSavePasswordVisible(visible) {
  clearTimeout(savePasswordTimer);
  savePassword.type = visible ? "text" : "password";
  toggleSavePassword.textContent = visible ? "Hide" : "Show";
  toggleSavePassword.setAttribute("aria-pressed", String(visible));
  if (visible) savePasswordTimer = setTimeout(() => setSavePasswordVisible(false), 10_000);
}

toggleSavePassword.addEventListener("click", () =>
  setSavePasswordVisible(savePassword.type === "password"));

async function currentPageUrl(tabId) {
  try {
    const tab = await chrome.tabs.get(tabId);
    const url = new URL(tab.url);
    url.search = "";
    url.hash = "";
    return url.toString();
  } catch {
    return "";
  }
}

function applyDefaultSaveMode(username) {
  const wanted = username.trim().toLowerCase();
  const matches = wanted
    ? matchingCredentials.filter((credential) => credential.username.trim().toLowerCase() === wanted)
    : [];

  refreshSaveTargets();

  if (matches.length === 1) {
    saveMode.value = "update";
    saveTarget.value = matches[0].id;
    saveTarget.hidden = false;
    if (!saveTitleEdited) saveTitle.value = matches[0].title || saveTitle.value;
  } else {
    saveMode.value = "create";
    saveTarget.value = "";
    saveTarget.hidden = true;
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

function addDiffRow(label, oldValue, newValue) {
  const row = document.createElement("li");
  row.className = "diff-row";

  const name = document.createElement("span");
  name.className = "diff-label";
  name.textContent = label + ": ";
  row.append(name);

  if (oldValue === newValue) {
    const same = document.createElement("span");
    same.className = "diff-same";
    same.textContent = newValue + " (unchanged)";
    row.append(same);
  } else {
    const before = document.createElement("span");
    before.className = "diff-old";
    before.textContent = oldValue;
    const after = document.createElement("span");
    after.className = "diff-new";
    after.textContent = newValue;
    row.append(before, " → ", after);
  }

  saveDiffList.append(row);
}

function renderSaveDiff(target) {
  saveDiffList.replaceChildren();
  if (saveMode.value !== "update" || !target) {
    saveDiff.hidden = true;
    return;
  }

  saveDiff.hidden = false;
  addDiffRow("Account name", target.title || "Untitled account", saveTitle.value.trim() || "Untitled account");
  addDiffRow("Username", target.username || "(blank)", saveUsername.value.trim() || "(blank)");
  addDiffRow("URL", target.url || "(none)", pendingSaveUrl || "(none)");

  const password = document.createElement("li");
  password.className = "diff-row";
  const name = document.createElement("span");
  name.className = "diff-label";
  name.textContent = "Password: ";
  password.append(name, "will be replaced. The previous one is kept in History.");
  saveDiffList.append(password);
}

function updateSaveReview() {
  if (saveFields.hidden) {
    saveLoginButton.disabled = true;
    return;
  }

  const title = saveTitle.value.trim();
  const username = saveUsername.value.trim();
  const password = savePassword.value;
  const mode = saveMode.value;
  const isUpdate = mode === "update";
  const target = matchingCredentials.find((credential) => credential.id === saveTarget.value);
  const matches = matchingCredentials.filter((credential) =>
    credential.username.trim().toLowerCase() === username.toLowerCase());

  saveConfirmationRow.hidden = !isUpdate;
  saveLoginButton.textContent = isUpdate ? "Update selected entry" : "Save as new entry";
  renderSaveDiff(target);

  const invalid = !title || !password || password.length > 4096 ||
    username.length > 256 || title.length > 200 || (isUpdate && !target);

  if (!title) {
    saveReviewNotice.textContent = "Enter an account name before saving.";
  } else if (!password) {
    saveReviewNotice.textContent = "A password is required before this login can be saved.";
  } else if (password.length > 4096 || username.length > 256 || title.length > 200) {
    saveReviewNotice.textContent = "Check the field lengths before saving.";
  } else if (isUpdate && !target) {
    saveReviewNotice.textContent = "Choose the exact saved account you want to update.";
  } else if (isUpdate) {
    saveReviewNotice.textContent = "Check the changes above, then confirm. This exact entry will be changed.";
  } else if (matches.length > 0) {
    saveReviewNotice.textContent = matches.length === 1
      ? "A saved account already uses this username. This will create a separate new entry; switch to Update to change it instead."
      : "Several saved accounts use this username. This will create a separate new entry.";
  } else if (!username) {
    saveReviewNotice.textContent = "The username is blank. Save only if this login legitimately has no username.";
  } else {
    saveReviewNotice.textContent = "This creates a new entry. Existing accounts are not changed.";
  }

  saveLoginButton.disabled = invalid || (isUpdate && !saveConfirmation.checked);
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
      (saveMode.value === "update" && saveConfirmation.checked !== true)) {
    saveStatus.textContent = "Complete the required fields and review the selected action before saving.";
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
    setSavePasswordVisible(false);
    saveMode.value = "create";
    saveTarget.value = "";
    saveTarget.hidden = true;
    saveConfirmation.checked = false;
    saveFields.hidden = true;
    saveBadge.hidden = true;
    saveTitleEdited = false;
    pendingSaveUrl = "";

    await lookupCredentials(activeDomain);
  } catch {
    saveStatus.textContent = "Could not save the login. Check that PejPass desktop and BrowserHost are running.";
  } finally {
    updateSaveReview();
  }
}

saveTitle.addEventListener("input", () => {
  saveTitleEdited = true;
  saveConfirmation.checked = false;
  updateSaveReview();
});
for (const field of [saveUsername, savePassword]) {
  field.addEventListener("input", () => {
    saveConfirmation.checked = false;
    updateSaveReview();
  });
}

saveMode.addEventListener("change", () => {
  saveConfirmation.checked = false;
  saveTarget.hidden = saveMode.value !== "update";
  if (saveMode.value === "update") refreshSaveTargets();
  else if (!saveTitleEdited) saveTitle.value = siteName.textContent || activeDomain || "";
  updateSaveReview();
});
saveTarget.addEventListener("change", () => {
  saveConfirmation.checked = false;
  const target = matchingCredentials.find((credential) => credential.id === saveTarget.value);
  if (target && !saveTitleEdited) saveTitle.value = target.title || saveTitle.value;
  updateSaveReview();
});
saveConfirmation.addEventListener("change", updateSaveReview);saveConfirmation.addEventListener("change", updateSaveReview);

fillNowButton.addEventListener("click", () => {
  if (matchingCredentials.length === 1) void fillCredential(matchingCredentials[0]);
});
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

function getSelectedCharacterSets() {
  return characterOptions
    .filter((option) => option.element.checked)
    .map((option) => excludeAmbiguous.checked
      ? option.characters.replace(/[0Oo1lI|\`]/g, "")
      : option.characters)
    .filter((set) => set.length > 0);
}

function generateSecurePassword() {
  const length = Number(passwordLength.value);
  const selectedSets = getSelectedCharacterSets();

  if (!Number.isInteger(length) || length < 8 || length > 64) {
    throw new Error("Choose a password length between 8 and 64.");
  }
  if (selectedSets.length === 0) {
    throw new Error("Select at least one character type with available characters.");
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

function updatePasswordStrength(password) {
  if (!password) {
    passwordStrength.textContent = "Strength estimate appears after generation.";
    return;
  }

  const alphabetSize = getSelectedCharacterSets().join("").length;
  const entropy = alphabetSize > 1 ? password.length * Math.log2(alphabetSize) : 0;
  const roundedBits = Math.round(entropy);
  const label = entropy < 40 ? "Low" : entropy < 60 ? "Moderate" : entropy < 80 ? "Strong" : "Very strong";
  passwordStrength.textContent = `Estimated strength: ${label} · ${password.length} characters · about ${roundedBits} bits`;
}

function setGeneratedPassword(password) {
  generatedPassword.value = password;
  copyPasswordButton.disabled = !password;
  updatePasswordStrength(password);
}

generatePasswordButton.addEventListener("click", () => {
  try {
    const password = generateSecurePassword();
    setGeneratedPassword(password);
    generatorStatus.textContent = "Generated locally in this browser. Not saved automatically.";
  } catch (error) {
    setGeneratedPassword("");
    generatorStatus.textContent = error instanceof Error
      ? error.message
      : "Could not generate a password.";
  }
});

async function fillGeneratedPassword(password) {
  if (!Number.isInteger(activeTabId) || !activeDomain) {
    return { ok: false, message: "Open a normal website tab before generating and filling a password." };
  }

  const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  const tab = tabs[0];
  if (!tab || tab.id !== activeTabId || typeof tab.url !== "string") {
    return { ok: false, message: "The active page changed. Reopen the popup and try again." };
  }

  let url;
  try { url = new URL(tab.url); } catch {
    return { ok: false, message: "The active page address is unavailable." };
  }
  const domain = url.hostname.toLowerCase().replace(/\.$/, "");
  if (domain !== activeDomain || !["http:", "https:"].includes(url.protocol)) {
    return { ok: false, message: "The active page changed. Reopen the popup and try again." };
  }

  const results = await chrome.scripting.executeScript({
    target: { tabId: activeTabId, allFrames: true },
    func: fillNewPasswordField,
    args: [activeDomain, password]
  });
  const outcomes = results || [];
  if (outcomes.some((entry) => entry.result === "filled")) {
    return { ok: true, message: "Generated password filled into the new-password field. The form was not submitted." };
  }
  if (outcomes.find((entry) => entry.frameId === 0)?.result === "wrong_page") {
    return { ok: false, message: "The page changed during filling. No password was entered." };
  }
  return { ok: false, message: "No suitable new-password field was found. The generated password is still available above." };
}

function fillNewPasswordField(expectedDomain, password) {
  const normalizeHost = (host) => host.toLowerCase().replace(/\.$/, "");
  const currentDomain = normalizeHost(window.location.hostname);
  let topDomain;
  try { topDomain = normalizeHost(window.top.location.hostname); }
  catch { return "wrong_page"; }

  if (topDomain !== expectedDomain ||
      (currentDomain !== expectedDomain && !currentDomain.endsWith("." + expectedDomain))) {
    return "wrong_page";
  }

  const isVisible = (element) => {
    const style = window.getComputedStyle(element);
    return style.display !== "none" && style.visibility !== "hidden" &&
      element.getClientRects().length > 0;
  };
  const inputs = Array.from(document.querySelectorAll("input"))
    .filter((input) => {
      const autocomplete = (input.autocomplete || "").toLowerCase().split(/\s+/);
      const isPasswordInput = (input.type || "").toLowerCase() === "password";
      return (isPasswordInput || autocomplete.includes("new-password")) &&
        !input.disabled && !input.readOnly && isVisible(input);
    });
  if (inputs.length === 0) return "no_password";

  const hints = (input) => [
    input.name, input.id, input.placeholder, input.getAttribute("aria-label"),
    input.getAttribute("data-testid")
  ].filter(Boolean).join(" ");
  let target = inputs.find((input) =>
    (input.autocomplete || "").toLowerCase().split(/\s+/).includes("new-password")) ||
    inputs.find((input) => /new.?password|password.?new|create.?password|register.?password|signup.?password/i.test(hints(input)));

  if (!target && inputs.length > 1 && inputs[0].form &&
      inputs.every((input) => input.form === inputs[0].form)) {
    target = inputs[0];
  }

  if (!target) {
    const form = inputs[0].form;
    const submitLabels = Array.from(
      (form || document).querySelectorAll('button, input[type="submit"], input[type="button"]')
    )
      .map((element) => [element.innerText, element.value, element.getAttribute("aria-label")].filter(Boolean).join(" "))
      .join(" ");
    const pageText = [
      document.title,
      document.body?.innerText?.slice(0, 6000) || "",
      submitLabels
    ].join(" ");
    const registrationPattern = /sign.?up|register|create.{0,12}account|new account|create.{0,12}password/i;
    const looksLikeRegistration = registrationPattern.test(pageText);
    const registrationSubmit = registrationPattern.test(submitLabels);
    const looksLikeLogin = /sign.?in|log.?in|forgot password|current password/i.test(pageText);

    if (inputs.length === 1 && (registrationSubmit || (looksLikeRegistration && !looksLikeLogin))) {
      target = inputs[0];
    } else if (inputs.length > 1 && inputs[0].form &&
        inputs.every((input) => input.form === inputs[0].form)) {
      target = inputs[0];
    }
  }

  if (!target) return "no_password";

  target.focus();
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  if (setter) setter.call(target, password);
  else target.value = password;
  target.dispatchEvent(new Event("input", { bubbles: true }));
  target.dispatchEvent(new Event("change", { bubbles: true }));
  target.blur();
  return "filled";
}

generateFillPasswordButton.addEventListener("click", async () => {
  let password;
  try {
    password = generateSecurePassword();
    setGeneratedPassword(password);
  } catch (error) {
    setGeneratedPassword("");
    generatorStatus.textContent = error instanceof Error
      ? error.message
      : "Could not generate a password.";
    return;
  }

  generateFillPasswordButton.disabled = true;
  generatorStatus.textContent = "Generating and checking the active page…";
  try {
    const result = await fillGeneratedPassword(password);
    generatorStatus.textContent = result.message;
  } catch (error) {
    const reason = error instanceof Error && error.message
      ? error.message
      : "The browser rejected page access.";
    generatorStatus.textContent = `Could not fill the page: ${reason} The generated password is still available above.`;
  } finally {
    generateFillPasswordButton.disabled = !Number.isInteger(activeTabId) || !activeDomain;
  }
});

copyPasswordButton.addEventListener("click", async () => {
  if (!generatedPassword.value) return;

  copyPasswordButton.disabled = true;
  generatorStatus.textContent = "Sending password to PejPass for timed clipboard copy…";
  try {
    const response = await sendNativeMessage({
      type: "copyToClipboard",
      protocolVersion,
      password: generatedPassword.value
    });
    if (isValidBaseResponse(response, "clipboardCopied") &&
        Number.isInteger(response.clipboardClearSeconds) &&
        response.clipboardClearSeconds >= 5 && response.clipboardClearSeconds <= 300) {
      copyPasswordButton.textContent = `Copy password (clears after ${response.clipboardClearSeconds} seconds)`;
      generatorStatus.textContent =
        `Password copied. PejPass will clear it after ${response.clipboardClearSeconds} seconds if it is still in the clipboard.`;
    } else {
      const messages = {
        app_unavailable: "Open PejPass desktop and try copying again.",
        unsupported_request: "Rebuild and reinstall the latest PejPass desktop and BrowserHost.",
        clipboard_unavailable: "PejPass could not access the clipboard. Try again or copy manually."
      };
      generatorStatus.textContent = messages[response?.code] ||
        `PejPass rejected the clipboard request (${response?.code || response?.type || "unexpected response"}). You can select and copy the password manually.`;
    }
  } catch {
    generatorStatus.textContent = "Could not connect to PejPass. Open the desktop app and try again, or copy manually.";
  } finally {
    copyPasswordButton.disabled = !generatedPassword.value;
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
    setSavePasswordVisible(false);
    saveFields.hidden = true;
    saveBadge.hidden = true;
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
  clearUnlockError();
  if (!unlockPassword.value || unlockPassword.value.length > 4096) {
    unlockStatus.textContent = "Enter your master password.";
    return;
  }

  unlockButton.disabled = true;
  launchAppButton.disabled = true;
  unlockStatus.textContent = unlockButton.textContent === "Launch & unlock"
    ? "Starting PejPass and verifying your password…"
    : "Verifying master password in PejPass…";
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
    if (response?.code !== "app_unavailable") unlockButton.textContent = "Unlock vault";
    showUnlockError(
      response?.code === "unlock_failed" ? "Password not accepted." :
        response?.code === "app_unavailable" ? "Couldn’t start PejPass." :
        "Couldn’t unlock the vault.",
      messages[response?.code] || response?.code || "The desktop connection failed or returned an incompatible response."
    );
  } catch (error) {
    showUnlockError("Couldn’t connect to PejPass.", error instanceof Error ? error.message : "The browser host could not be reached.");
  } finally {
    unlockPassword.value = "";
    unlockButton.disabled = unlockCoolingDown;
    if (!unlockCoolingDown && unlockButton.textContent !== "Launch & unlock") unlockButton.textContent = "Unlock vault";
    launchAppButton.disabled = false;
  }
}

async function unlockWithHello() {
  clearUnlockError();
  unlockHelloButton.disabled = true;
  unlockHelloButton.textContent = "Waiting for Windows Hello…";
  unlockStatus.textContent = "Confirm your identity in Windows Hello.";
  try {
    const response = await sendNativeMessage({ type: "unlockWithHello", protocolVersion });
    if (isValidBaseResponse(response, "vaultUnlocked") && response.status === "unlocked") {
      unlockStatus.textContent = "Vault unlocked.";
      await checkVaultStatus();
      if (activeDomain) await lookupCredentials(activeDomain);
      return;
    }

    const messages = {
      hello_failed: "Windows Hello didn’t unlock the vault. Try your master password.",
      hello_cancelled: "Windows Hello was cancelled.",
      hello_unavailable: "Windows Hello isn’t available. Try your master password.",
      app_unavailable: "Couldn’t start PejPass.",
      unsupported_request: "Update the PejPass desktop app and browser host."
    };
    showUnlockError(
      response?.code === "hello_cancelled" ? "Windows Hello cancelled." :
        response?.code === "hello_unavailable" ? "Windows Hello unavailable." :
        "Couldn’t unlock with Windows Hello.",
      messages[response?.code] || response?.code || "The desktop returned an invalid response."
    );
  } catch (error) {
    showUnlockError("Couldn’t connect to PejPass.", error instanceof Error ? error.message : "The browser host could not be reached.");
  } finally {
    unlockHelloButton.disabled = false;
    unlockHelloButton.textContent = "Unlock with Windows Hello";
  }
}

unlockButton.addEventListener("click", unlockVault);
unlockHelloButton.addEventListener("click", unlockWithHello);
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
checkVaultStatus();

chrome.runtime.onMessage.addListener((message) => {
  if (message?.type !== "vaultStateChanged") return;

  void checkVaultStatus().then((status) => {
    if (status === "unlocked" && activeDomain && matchingCredentials.length === 0) {
      return lookupCredentials(activeDomain);
    }
  });
});
