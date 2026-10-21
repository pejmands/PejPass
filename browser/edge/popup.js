const siteName = document.getElementById("site-name");
const siteDetail = document.getElementById("site-detail");
const version = document.getElementById("version");
const connectionStatus = document.getElementById("connection-status");
const connectionDot = document.getElementById("connection-dot");

const nativeHostName = "com.pejpass.browserhost";
const protocolVersion = 1;

version.textContent = chrome.runtime.getManifest().version;

function showUnavailable(message) {
  siteName.textContent = "Unavailable page";
  siteDetail.textContent = message;
}

function setConnectionStatus(connected, message) {
  connectionStatus.textContent = message;
  connectionDot.classList.toggle("connected", connected);
  connectionDot.classList.toggle("disconnected", !connected);
}

async function showActiveSite() {
  try {
    const tabs = await chrome.tabs.query({
      active: true,
      lastFocusedWindow: true
    });
    const tab = tabs[0];

    if (!tab || typeof tab.url !== "string") {
      showUnavailable("The active tab address is not available.");
      return;
    }

    let url;

    try {
      url = new URL(tab.url);
    } catch {
      showUnavailable("The active tab address could not be read.");
      return;
    }

    if (url.protocol !== "http:" && url.protocol !== "https:") {
      showUnavailable("Edge does not allow extensions to inspect this page.");
      return;
    }

    siteName.textContent = url.hostname;
    siteDetail.textContent = "Site detected. Credential features are not connected yet.";
  } catch {
    showUnavailable("Could not read the active tab. Try closing and reopening the popup.");
  }
}

function checkDesktopConnection() {
  try {
    chrome.runtime.sendNativeMessage(
      nativeHostName,
      { type: "ping", protocolVersion },
      (response) => {
        const error = chrome.runtime.lastError;

        if (error) {
          setConnectionStatus(false, "Desktop bridge not installed or unavailable.");
          return;
        }

        if (
          response?.type === "pong" &&
          response?.app === "PejPass" &&
          response?.protocolVersion === protocolVersion
        ) {
          setConnectionStatus(true, "PejPass desktop bridge connected.");
          return;
        }

        setConnectionStatus(false, "Desktop bridge returned an unexpected response.");
      }
    );
  } catch {
    setConnectionStatus(false, "Could not contact the PejPass desktop bridge.");
  }
}

showActiveSite();
checkDesktopConnection();
