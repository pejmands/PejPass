const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const assert = require("node:assert/strict");

const backgroundSource = fs.readFileSync(path.join(__dirname, "..", "background.js"), "utf8");
const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, "..", "manifest.json"), "utf8"));
const firefoxManifest = JSON.parse(
  fs.readFileSync(path.join(__dirname, "..", "..", "firefox", "manifest.json"), "utf8"));

test("background persists the cached vault state in storage.session and restores it on start", () => {
  assert.match(backgroundSource, /storage\?\.session/);
  assert.match(backgroundSource, /function persistState\(\)/);
  assert.match(backgroundSource, /async function restoreState\(\)/);
  assert.match(backgroundSource, /cachedHelloReady = helloReady;\s*persistState\(\);/);
  assert.match(backgroundSource, /void restoreState\(\);\s*\/\/ Re-establish the native connection/);
});

test("persisted state never contains credentials", () => {
  const persisted = backgroundSource.match(/pejpassState:\s*\{([^}]*)\}/);
  assert.ok(persisted);
  assert.doesNotMatch(persisted[1], /password|totp|username|domain|url/i);
});

test("Chromium and Firefox manifests use PNG toolbar icons and the storage permission", () => {
  for (const candidate of [manifest, firefoxManifest]) {
    assert.equal(typeof candidate.action.default_icon, "object");
    assert.ok(Object.values(candidate.action.default_icon).every((file) => file.endsWith(".png")));
    assert.ok(candidate.permissions.includes("storage"));
  }
});
