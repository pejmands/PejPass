const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const { JSDOM } = require("jsdom");

const injectedSource = fs.readFileSync(path.join(__dirname, "..", "..", "shared", "injected.js"), "utf8");
const packagedInjectedSource = fs.readFileSync(path.join(__dirname, "..", "injected.js"), "utf8");

function createPage(markup, url = "https://example.com/login") {
  const dom = new JSDOM(markup, {
    url,
    runScripts: "outside-only",
    pretendToBeVisual: true
  });
  dom.window.HTMLElement.prototype.getClientRects = function () {
    return [{ width: 100, height: 20, top: 0, left: 0, right: 100, bottom: 20 }];
  };
  dom.window.eval(injectedSource);
  return dom;
}

test("Edge package contains the current shared login-form implementation", () => {
  assert.equal(packagedInjectedSource, injectedSource);
});

test("probe detects a visible password without returning its value", () => {
  const dom = createPage('<form><input name="username" autocomplete="username" value="alice"><input type="password" name="password" value="secret"></form>');
  try {
    const result = dom.window.processLoginForm("probe", "example.com");
    assert.deepEqual({ found: result.found, domain: result.domain, hasPassword: result.hasPassword },
      { found: true, domain: "example.com", hasPassword: true });
    assert.equal("password" in result, false);
  } finally {
    dom.window.close();
  }
});

test("fills a same-site subdomain frame using ancestorOrigins", () => {
  const dom = createPage('<form><input type="password" name="password"></form>', "https://login.example.com/login");
  try {
    Object.defineProperty(dom.window.location, "ancestorOrigins", {
      configurable: true,
      value: ["https://example.com"]
    });
    const result = dom.window.processLoginForm("fill", "example.com", "", "frame-secret");
    assert.equal(result, "no_username");
    assert.equal(dom.window.document.querySelector('input[type="password"]').value, "frame-secret");
  } finally {
    dom.window.close();
  }
});

test("detects and fills a username-only first step", () => {
  const dom = createPage('<form><input type="email" name="email" autocomplete="username"></form>');
  try {
    const detected = dom.window.processLoginForm("read", "example.com");
    assert.equal(detected.found, true);
    assert.equal(detected.username, "");
    assert.equal(dom.window.processLoginForm("fill", "example.com", "alice@example.com", "unused"), "username_only");
    assert.equal(dom.window.document.querySelector('input[name="email"]').value, "alice@example.com");
  } finally {
    dom.window.close();
  }
});

test("fills one-digit OTP inputs individually and preserves leading zeroes", () => {
  const dom = createPage('<form><input name="otp1" autocomplete="one-time-code" maxlength="1"><input name="otp2" aria-label="OTP" maxlength="1"><input name="otp3" autocomplete="one-time-code" maxlength="1"><input name="otp4" autocomplete="one-time-code" maxlength="1"><input name="otp5" autocomplete="one-time-code" maxlength="1"><input name="otp6" autocomplete="one-time-code" maxlength="1"></form>');
  try {
    const result = dom.window.processLoginForm("fill", "example.com", "", "", "012345");
    assert.equal(result, "totp_filled");
    assert.deepEqual(
      [...dom.window.document.querySelectorAll("input")].map(input => input.value),
      ["0", "1", "2", "3", "4", "5"]
    );
  } finally {
    dom.window.close();
  }
});

test("three-field password-change forms only fill the current-password field", () => {
  const dom = createPage('<form><input name="username" autocomplete="username"><input type="password" name="currentPassword" autocomplete="current-password"><input type="password" name="newPassword" autocomplete="new-password"><input type="password" name="confirmPassword" autocomplete="new-password"></form>');
  try {
    const result = dom.window.processLoginForm("fill", "example.com", "alice", "old-secret");
    assert.equal(result, "filled");
    const fields = [...dom.window.document.querySelectorAll('input[type="password"]')];
    assert.deepEqual(fields.map(input => input.value), ["old-secret", "", ""]);
  } finally {
    dom.window.close();
  }
});
