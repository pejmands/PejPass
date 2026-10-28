const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const assert = require("node:assert/strict");

const repositoryRoot = path.join(__dirname, "..", "..", "..");
const browserRoot = path.join(repositoryRoot, "browser");
const chromiumRoot = path.join(browserRoot, "chromium");
const chromiumManifest = JSON.parse(fs.readFileSync(path.join(chromiumRoot, "manifest.json"), "utf8"));
const buildScript = fs.readFileSync(path.join(browserRoot, "build.ps1"), "utf8");
const installScript = fs.readFileSync(path.join(chromiumRoot, "install-dev-host.ps1"), "utf8");
const uninstallScript = fs.readFileSync(path.join(chromiumRoot, "uninstall-dev-host.ps1"), "utf8");
const registrationCheck = fs.readFileSync(path.join(browserRoot, "check-host-registration.ps1"), "utf8");

test("Chromium manifest is MV3 and retains the stable Chromium extension identity", () => {
  assert.equal(chromiumManifest.manifest_version, 3);
  assert.equal(chromiumManifest.minimum_chrome_version, "105");
  assert.equal(chromiumManifest.background.service_worker, "background.js");
  assert.ok(chromiumManifest.key);
  assert.ok(chromiumManifest.permissions.includes("nativeMessaging"));
  assert.ok(chromiumManifest.permissions.includes("scripting"));
});

test("only Chromium and Firefox have build targets and separate output directories", () => {
  assert.match(buildScript, /ValidateSet\("chromium",\s*"firefox"\)/);
  assert.match(buildScript, /Join-Path \$browserRoot "dist\\chromium"/);
  assert.match(buildScript, /Join-Path \$browserRoot "dist\\firefox"/);
  assert.doesNotMatch(buildScript, /dist\\edge|dist\\chrome/);
  assert.equal(fs.existsSync(path.join(browserRoot, "edge")), false);
  assert.equal(fs.existsSync(path.join(browserRoot, "chrome")), false);
});

test("Chromium build copies the shared runtime and injected form logic", () => {
  assert.match(buildScript, /Join-Path \$browserRoot "chromium"/);
  assert.match(buildScript, /Copy-Item -LiteralPath \$sourcePath -Destination \(Join-Path \$targetDirectory "injected\.js"\)/);
  assert.match(buildScript, /if \(\$Browser -eq "firefox"\)/);
});

test("one Chromium host manifest is registered for both Edge and Chrome", () => {
  assert.match(installScript, /Software\\Microsoft\\Edge\\NativeMessagingHosts/);
  assert.match(installScript, /Software\\Google\\Chrome\\NativeMessagingHosts/);
  assert.match(installScript, /allowed_origins\s*=\s*@\("chrome-extension:\/\/\$ExtensionId\/"\)/);
  assert.match(installScript, /edge:\/\/extensions or chrome:\/\/extensions/);
  assert.match(installScript, /desktopAppPath\s*=\s*\$desktopExecutable/);
  assert.match(installScript, /foreach \(\$registryPath in \$registryPaths\)/);
});

test("Chromium uninstall removes both registry entries and only deletes an unregistered manifest", () => {
  assert.match(uninstallScript, /Software\\Microsoft\\Edge\\NativeMessagingHosts/);
  assert.match(uninstallScript, /Software\\Google\\Chrome\\NativeMessagingHosts/);
  assert.match(uninstallScript, /foreach \(\$registryPath in \$registryPaths\)/);
  assert.match(uninstallScript, /-not \$stillRegistered/);
});

test("registration diagnostics include both Chromium browsers and Firefox", () => {
  assert.match(registrationCheck, /Browser = "Edge"/);
  assert.match(registrationCheck, /Browser = "Chrome"/);
  assert.match(registrationCheck, /Browser = "Firefox"/);
});
