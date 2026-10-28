const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const assert = require("node:assert/strict");

const repositoryRoot = path.join(__dirname, "..", "..", "..");
const edgeRoot = path.join(repositoryRoot, "browser", "edge");
const chromeRoot = path.join(repositoryRoot, "browser", "chrome");
const chromeManifest = JSON.parse(fs.readFileSync(path.join(chromeRoot, "manifest.json"), "utf8"));
const edgeManifest = JSON.parse(fs.readFileSync(path.join(edgeRoot, "manifest.json"), "utf8"));
const buildScript = fs.readFileSync(path.join(repositoryRoot, "browser", "build.ps1"), "utf8");
const installScript = fs.readFileSync(path.join(chromeRoot, "install-dev-host.ps1"), "utf8");
const uninstallScript = fs.readFileSync(path.join(chromeRoot, "uninstall-dev-host.ps1"), "utf8");
const edgeUninstallScript = fs.readFileSync(path.join(edgeRoot, "uninstall-dev-host.ps1"), "utf8");
const registrationCheck = fs.readFileSync(path.join(repositoryRoot, "browser", "check-host-registration.ps1"), "utf8");

test("Chrome manifest is MV3 and retains the stable Chromium extension identity", () => {
  assert.equal(chromeManifest.manifest_version, 3);
  assert.equal(chromeManifest.key, edgeManifest.key);
  assert.equal(chromeManifest.minimum_chrome_version, "105");
  assert.equal(chromeManifest.background.service_worker, "background.js");
  assert.ok(chromeManifest.permissions.includes("nativeMessaging"));
  assert.ok(chromeManifest.permissions.includes("scripting"));
});

test("Each browser builds an isolated package under its own dist directory", () => {
  assert.match(buildScript, /Join-Path \$browserRoot "dist\\edge"/);
  assert.match(buildScript, /Join-Path \$browserRoot "dist\\chrome"/);
  assert.match(buildScript, /Join-Path \$browserRoot "dist\\firefox"/);
  assert.match(buildScript, /"test-host-reconnect\.ps1"/);
});

test("Chrome build reuses the Edge runtime and shared injected script", () => {
  assert.match(buildScript, /ValidateSet\("edge",\s*"chrome",\s*"firefox"\)/);
  assert.match(buildScript, /Join-Path \$browserRoot "dist\\chrome"/);
  assert.match(buildScript, /Join-Path \$browserRoot "chrome"/);
  assert.match(buildScript, /Copy-Item -LiteralPath \$sourcePath -Destination \(Join-Path \$targetDirectory "injected\.js"\)/);
  assert.match(buildScript, /if \(\$Browser -eq "firefox"\)/);
});

test("Chrome registers the native host using Chrome's per-user registry key", () => {
  assert.match(installScript, /Software\\Google\\Chrome\\NativeMessagingHosts/);
  assert.match(installScript, /allowed_origins\s*=\s*@\("chrome-extension:\/\/\$ExtensionId\/"\)/);
  assert.match(installScript, /chrome:\/\/extensions/);
  assert.match(installScript, /desktopAppPath\s*=\s*\$desktopExecutable/);
});

test("Removing either Chromium registration preserves the shared manifest while the other browser remains registered", () => {
  assert.match(uninstallScript, /Software\\Microsoft\\Edge\\NativeMessagingHosts/);
  assert.match(uninstallScript, /-not \(Test-Path -LiteralPath \$edgeRegistryPath\)/);
  assert.match(edgeUninstallScript, /Software\\Google\\Chrome\\NativeMessagingHosts/);
  assert.match(edgeUninstallScript, /-not \(Test-Path -LiteralPath \$chromeRegistryPath\)/);
});

test("Native host diagnostics include Chrome", () => {
  assert.match(registrationCheck, /Browser = "Chrome"/);
  assert.match(registrationCheck, /Software\\Google\\Chrome\\NativeMessagingHosts/);
});
