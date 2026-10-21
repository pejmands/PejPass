const siteName = document.getElementById("site-name");
const siteDetail = document.getElementById("site-detail");
const version = document.getElementById("version");

version.textContent = chrome.runtime.getManifest().version;

function showUnavailable(message) {
  siteName.textContent = "Unavailable page";
  siteDetail.textContent = message;
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
    siteDetail.textContent = "Site detected. Password features are not connected yet.";
  } catch {
    showUnavailable("Could not read the active tab. Try closing and reopening the popup.");
  }
}

showActiveSite();
