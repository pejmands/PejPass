# PejPass for Microsoft Edge

This directory contains the first, deliberately minimal browser-extension shell for PejPass. It does not connect to the vault, read credentials, fill forms, or make network requests.

## Requirements

- Microsoft Edge
- A local checkout of this repository

## Load the extension

1. Open `edge://extensions` in Edge.
2. Enable **Developer mode**.
3. Select **Load unpacked**.
4. Choose the `browser/edge` directory.
5. Pin PejPass if desired, then open the extension popup on an ordinary website.

## Manual checks

- The PejPass popup opens and displays its version.
- On an `http://` or `https://` page, the popup displays the active tab's hostname.
- On restricted pages such as `edge://extensions`, the popup displays a clear unavailable-page message rather than failing.
- The extension requests only the `activeTab` permission.
- No credentials, page contents, or browsing history are collected or transmitted.

## Current scope

This is only the UI and active-site detection foundation. Native messaging and any connection to the PejPass vault are intentionally out of scope for this step.
