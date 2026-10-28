const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const assert = require("node:assert/strict");

const root = path.join(__dirname, "..");
const browserApiSource = fs.readFileSync(path.join(root, "browser-api.js"), "utf8");
const backgroundSource = fs.readFileSync(path.join(root, "background.js"), "utf8");
const popupSource = fs.readFileSync(path.join(root, "popup.js"), "utf8");
const apiSource = fs.readFileSync(path.join(root, "api.js"), "utf8");
const popupHtml = fs.readFileSync(path.join(root, "popup.html"), "utf8");

test("browser API facade prefers the standard browser namespace and falls back to chrome", () => {
  assert.match(browserApiSource, /globalThis\.browser\s*\?\?\s*globalThis\.chrome/);
  assert.match(browserApiSource, /globalThis\.pejpassBrowserApi\s*=\s*browserApi/);
});

test("background loads the browser API facade before using it", () => {
  assert.match(backgroundSource, /^importScripts\("browser-api\.js", "injected\.js"\);/);
  assert.match(backgroundSource, /globalThis\.pejpassBrowserApi\./);
  assert.doesNotMatch(backgroundSource, /\bchrome\./);
});

test("popup loads the facade before its scripts and uses the facade", () => {
  const facadeIndex = popupHtml.indexOf('src="browser-api.js"');
  const apiIndex = popupHtml.indexOf('src="api.js"');
  const popupIndex = popupHtml.indexOf('src="popup.js"');
  assert.ok(facadeIndex >= 0 && facadeIndex < apiIndex && apiIndex < popupIndex);
  assert.match(popupSource, /globalThis\.pejpassBrowserApi\./);
  assert.match(apiSource, /globalThis\.pejpassBrowserApi\./);
  assert.doesNotMatch(popupSource, /\bchrome\./);
  assert.doesNotMatch(apiSource, /\bchrome\./);
});
