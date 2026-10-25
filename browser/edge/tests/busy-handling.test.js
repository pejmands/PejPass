const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const assert = require("node:assert/strict");

const root = path.join(__dirname, "..");
const popupSource = fs.readFileSync(path.join(root, "popup.js"), "utf8");
const popupUiSource = fs.readFileSync(path.join(root, "popup-ui.js"), "utf8");
const hostSource = fs.readFileSync(
  path.join(root, "..", "..", "src", "PejPass.BrowserHost", "Program.cs"), "utf8");

test("BrowserHost answers app_busy instead of queueing indefinitely on the desktop pipe", () => {
  assert.match(hostSource, /PipeRequestGate\.WaitAsync\(GateWaitFor\(request\.Type\), cancellationToken\)/);
  assert.match(hostSource, /new PipeResponse\(default, "app_busy"\)/);
  assert.doesNotMatch(hostSource, /await PipeRequestGate\.WaitAsync\(cancellationToken\)/);
});

test("popup shows a busy message for app_busy instead of a connection error", () => {
  assert.match(popupSource, /const busyMessage = /);
  assert.ok((popupSource.match(/app_busy: busyMessage,/g) || []).length >= 6);
  assert.match(popupSource, /response\?\.code === "app_busy"/);
});

test("busy messages are styled as warnings", () => {
  assert.match(popupUiSource, /\\bbusy\\b/);
});
