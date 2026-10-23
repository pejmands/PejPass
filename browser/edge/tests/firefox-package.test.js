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

test("Firefox manifest declares its stable Gecko ID and supported MV3 background scripts", () => {
  assert.equal(manifest.manifest_version, 3);
  assert.equal(manifest.browser_specific_settings.gecko.id, "pejpass@pejmands.github.io");
  assert.deepEqual(manifest.background.scripts, ["browser-api.js", "injected.js", "background.js"]);
  assert.equal(manifest.background.service_worker, undefined);
  assert.ok(manifest.permissions.includes("nativeMessaging"));
});

test("Firefox build copies the Edge runtime and removes the worker-only importScripts line", () => {
  assert.match(buildScript, /ValidateSet\("edge",\s*"firefox"\)/);
  assert.ok(buildScript.includes('importScripts("browser-api.js", "injected.js");'));
  assert.match(buildScript, /StartsWith\(\$workerImport\)/);
  assert.match(backgroundSource, /^importScripts\("browser-api\.js", "injected\.js"\);/);
});

test("Firefox native host registration uses the Gecko ID and a separate output directory", () => {
  assert.match(installScript, /allowed_extensions\s*=\s*@\(\$extensionId\)/);
  assert.match(installScript, /Software\\Mozilla\\NativeMessagingHosts/);
  assert.match(installScript, /\\Firefox/);
  assert.match(installScript, /pejpass@pejmands\.github\.io/);
});
