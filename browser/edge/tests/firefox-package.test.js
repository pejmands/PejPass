const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const assert = require("node:assert/strict");

const repositoryRoot = path.join(__dirname, "..", "..", "..");
const firefoxRoot = path.join(repositoryRoot, "browser", "firefox");
const manifest = JSON.parse(fs.readFileSync(path.join(firefoxRoot, "manifest.json"), "utf8"));
const buildScript = fs.readFileSync(path.join(repositoryRoot, "browser", "build.ps1"), "utf8");
const backgroundSource = fs.readFileSync(path.join(__dirname, "..", "background.js"), "utf8");
const installScript = fs.readFileSync(path.join(firefoxRoot, "install-dev-host.ps1"), "utf8");
const signingScript = fs.readFileSync(path.join(firefoxRoot, "sign-addon.ps1"), "utf8");
const firefoxReadme = fs.readFileSync(path.join(firefoxRoot, "README.md"), "utf8");

test("Firefox manifest declares its stable Gecko ID and supported MV3 background scripts", () => {
  assert.equal(manifest.manifest_version, 3);
  assert.equal(manifest.browser_specific_settings.gecko.id, "pejpass@pejmands.github.io");
  assert.deepEqual(manifest.background.scripts, ["browser-api.js", "injected.js", "background.js"]);
  assert.equal(manifest.background.service_worker, undefined);
  assert.ok(manifest.permissions.includes("nativeMessaging"));
  assert.equal(manifest.desktopAppPath, undefined);
});

test("Firefox build copies the Edge runtime and removes the worker-only importScripts line", () => {
  assert.match(buildScript, /ValidateSet\("edge",\s*"chrome",\s*"firefox"\)/);
  assert.ok(buildScript.includes('importScripts("browser-api.js", "injected.js");'));
  assert.match(buildScript, /StartsWith\(\$workerImport\)/);
  assert.match(backgroundSource, /^importScripts\("browser-api\.js", "injected\.js"\);/);
});

test("Firefox native host registration uses the Gecko ID and a separate output directory", () => {
  assert.match(installScript, /allowed_extensions\s*=\s*@\(\$extensionId\)/);
  assert.match(installScript, /Software\\Mozilla\\NativeMessagingHosts/);
  assert.match(installScript, /firefoxHostDirectory\s*=\s*Join-Path\s+\$outputDirectory\s+"Firefox"/);
  assert.match(installScript, /pejpass@pejmands\.github\.io/);
  const nativeManifestBlock = installScript.match(/\$manifest\s*=\s*@\{([\s\S]*?)\}\s*\|\s*ConvertTo-Json/);
  assert.ok(nativeManifestBlock);
  assert.doesNotMatch(nativeManifestBlock[1], /desktopAppPath/);
  assert.match(installScript, /pejpass-host-config\.json/);
  assert.match(installScript, /desktopAppPath\s*=\s*\$desktopExecutable/);
});

test("Firefox signing script requests unlisted Mozilla signing without embedding credentials", () => {
  assert.match(signingScript, /WEB_EXT_API_KEY/);
  assert.match(signingScript, /WEB_EXT_API_SECRET/);
  assert.match(signingScript, /npx --yes web-ext sign/);
  assert.match(signingScript, /--channel unlisted/);
  assert.match(signingScript, /--source-dir \$sourceDirectory/);
  assert.match(signingScript, /--artifacts-dir \$artifactsDirectory/);
  assert.match(signingScript, /--api-secret \$env:WEB_EXT_API_SECRET/);
  assert.match(signingScript, /signed-firefox/);
  assert.match(firefoxReadme, /Install Add-on From File/);
  assert.match(firefoxReadme, /pejpass@pejmands\.github\.io/);
});

test("Both browsers use the same PNG-based toolbar icon renderer", () => {
  assert.match(backgroundSource, /async function loadToolbarIcon\(unlocked\)/);
  assert.match(backgroundSource, /icons\/PejPass128\.png/);
  assert.match(backgroundSource, /if \(!unlocked\) \{/);
  assert.match(backgroundSource, /const imageData = await loadToolbarIcon\(unlocked\);/);
  assert.doesNotMatch(backgroundSource, /loadFirefoxToolbarIcon|PejPassLocked\.ico|PejPass\.ico/);
  assert.doesNotMatch(backgroundSource, /const isFirefox =/);
});
