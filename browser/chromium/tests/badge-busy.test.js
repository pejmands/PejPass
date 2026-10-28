const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const assert = require("node:assert/strict");

const backgroundSource = fs.readFileSync(path.join(__dirname, "..", "background.js"), "utf8");

test("refreshMatchCount restores the previous badge count when the desktop is busy", () => {
  assert.match(backgroundSource,
    /matchRefreshVersions\.set\(tabId, version\);\s*const previousCount = matchCounts\.get\(tabId\) \|\| 0;\s*clearMatchBadge\(tabId\);/);
  assert.match(backgroundSource,
    /response\?\.type === "error" && response\.code === "app_busy"[\s\S]*?setMatchBadge\(tabId, previousCount\);\s*return;/);
});

test("the busy path only restores the badge for the still-current refresh", () => {
  assert.match(backgroundSource,
    /if \(matchRefreshVersions\.get\(tabId\) === version\) setMatchBadge\(tabId, previousCount\);/);
});
