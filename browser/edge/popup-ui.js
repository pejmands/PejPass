// Presentation-only helpers for the popup. Loaded after popup.js.
// 1) Colours status messages (error / warning / success / info) without changing popup.js logic.
// 2) Provides the About / diagnostics panel opened from the version button.
(() => {
  const api = globalThis.pejpassBrowserApi;

  // ---------- Semantic message classification ----------
  const patterns = {
    // "Login fields filled, but …" is a partial result, not a failure.
    warningFirst: /\bbut\b/i,
    success: /\b(filled|saved|updated|copied|unlocked|connected|nothing changed)\b/i,
    error: new RegExp([
      "couldn.?t", "could not", "cannot", "can.?t", "not accepted", "invalid", "failed",
      "unavailable", "cancel+ed", "outdated", "incompatible", "no visible", "no suitable",
      "no longer", "rejected", "too many", "isn.?t running", "isn.?t ready", "not found",
      "connection error", "(?<!not |nothing |be )changed", "was cancelled", "did not verify",
      "rebuild", "no password"
    ].join("|"), "i"),
    warning: /^(unlock|open pejpass|enter |choose |read and|complete |a password is required|the username is blank|vault (is )?locked|check the)|\blocked\b|\bbusy\b|waiting for you/i
  };

  function classify(text) {
    const value = (text || "").trim();
    if (!value || value.endsWith("…")) return "info";
    if (patterns.warningFirst.test(value)) return "warning";
    if (patterns.success.test(value)) return "success";
    if (patterns.error.test(value)) return "error";
    if (patterns.warning.test(value)) return "warning";
    return "info";
  }

  const kinds = ["kind-error", "kind-warning", "kind-success", "kind-info"];
  function applyKind(element) {
    const kind = classify(element.textContent);
    element.classList.remove(...kinds);
    element.classList.add("kind-" + kind);
    element.setAttribute("role", kind === "error" ? "alert" : "status");
  }

  const watchedIds = [
    "connection-status", "vault-status", "credentials-status", "credential-search-status",
    "unlock-status", "save-status", "save-review-notice", "generator-status", "diagnostics-status"
  ];
  const observer = new MutationObserver((records) => {
    for (const element of new Set(records.map((record) => record.target.nodeType === 1
      ? record.target : record.target.parentElement))) {
      if (element?.id && watchedIds.includes(element.id)) applyKind(element);
    }
  });
  for (const id of watchedIds) {
    const element = document.getElementById(id);
    if (!element) continue;
    applyKind(element);
    observer.observe(element, { childList: true, characterData: true, subtree: true });
  }

  // ---------- About / diagnostics ----------
  const toggle = document.getElementById("version");
  const panel = document.getElementById("about-panel");
  const copyButton = document.getElementById("copy-diagnostics");
  const copyStatus = document.getElementById("diagnostics-status");
  const field = (id) => document.getElementById(id);

  function browserName() {
    const ua = navigator.userAgent;
    const firefox = ua.match(/Firefox\/([\d.]+)/);
    if (firefox) return "Firefox " + firefox[1];
    const edge = ua.match(/Edg\/([\d.]+)/);
    if (edge) return "Edge " + edge[1];
    const chrome = ua.match(/Chrome\/([\d.]+)/);
    return chrome ? "Chromium " + chrome[1] : "Unknown";
  }

  async function refreshAbout() {
    field("about-version").textContent = api.runtime.getManifest().version;
    field("about-browser").textContent = browserName();
    field("about-desktop").textContent = field("connection-status").textContent || "Unknown";
    field("about-vault").textContent = field("vault-status").textContent || "Unknown";
    try {
      const state = await api.runtime.sendMessage({ target: "pejpass-state" });
      field("about-host-error").textContent = state?.nativeHostError || "None";
    } catch {
      field("about-host-error").textContent = "Background script not reachable";
    }
  }

  toggle.textContent = api.runtime.getManifest().version;
  toggle.addEventListener("click", async () => {
    const open = panel.hidden;
    panel.hidden = !open;
    toggle.setAttribute("aria-expanded", String(open));
    if (open) await refreshAbout();
  });

  copyButton.addEventListener("click", async () => {
    await refreshAbout();
    const lines = [
      "PejPass diagnostics",
      "Extension: " + field("about-version").textContent,
      "Browser: " + field("about-browser").textContent,
      "Desktop: " + field("about-desktop").textContent,
      "Vault: " + field("about-vault").textContent,
      "Host error: " + field("about-host-error").textContent
    ];
    try {
      await navigator.clipboard.writeText(lines.join("\n"));
      copyStatus.textContent = "Diagnostics copied.";
    } catch {
      copyStatus.textContent = "Could not copy. Select the details above and copy them manually.";
    }
  });
})();
