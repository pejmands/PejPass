function processLoginForm(action, expectedDomain, username = "", password = "", totpCode = "") {
  const normalizeHost = (host) => host.toLowerCase().replace(/\.$/, "");
  const currentDomain = normalizeHost(window.location.hostname);
  let topDomain;
  try {
    const ancestors = window.location.ancestorOrigins;
    const topOrigin = ancestors && ancestors.length > 0
      ? ancestors[ancestors.length - 1]
      : window.top.location.origin;
    topDomain = normalizeHost(new URL(topOrigin).hostname);
  } catch {
    return action === "read" ? { found: false } : "wrong_page";
  }

  if (topDomain !== expectedDomain ||
      (currentDomain !== expectedDomain && !currentDomain.endsWith("." + expectedDomain))) {
    return action === "read" ? { found: false } : "wrong_page";
  }

  const isVisible = (element) => {
    const style = window.getComputedStyle(element);
    return style.display !== "none" && style.visibility !== "hidden" &&
      element.getClientRects().length > 0;
  };
  const usable = (input) => !input.disabled && !input.readOnly && isVisible(input);
  const collectInputs = (root) => {
    const found = Array.from(root.querySelectorAll("input"));
    for (const element of root.querySelectorAll("*")) {
      if (element.shadowRoot) found.push(...collectInputs(element.shadowRoot));
    }
    return found;
  };
  const inputs = collectInputs(document).filter(usable);
  const passwordHints = (input) => [
    input.name, input.id, input.placeholder, input.getAttribute("aria-label"),
    input.getAttribute("data-testid"), input.getAttribute("data-field")
  ].filter(Boolean).join(" ");
  const isPasswordField = (input) => {
    const type = (input.type || "").toLowerCase();
    const autocomplete = (input.autocomplete || "").toLowerCase().split(/\s+/);
    return type === "password" ||
      autocomplete.includes("current-password") ||
      autocomplete.includes("new-password") ||
      /\b(pass(word|wd)?|pwd)\b/i.test(passwordHints(input));
  };
  const isTotpField = (input) => {
    const autocomplete = (input.autocomplete || "").toLowerCase().split(/\s+/);
    const hints = [
      input.name, input.id, input.placeholder, input.getAttribute("aria-label"),
      input.getAttribute("inputmode"), input.getAttribute("data-testid")
    ].filter(Boolean).join(" ");
    return autocomplete.includes("one-time-code") ||
      /one.?time|otp|totp|auth.?code|verification.?code|security.?code|mfa.?code/i.test(hints);
  };
  const isTextLike = (input) =>
    ["text", "email", "tel", "url", "search", ""].includes((input.type || "text").toLowerCase());
  const passwordField = inputs.find((input) => (input.type || "").toLowerCase() === "password") ||
    inputs.find(isPasswordField);
  const formRoot = passwordField?.form || passwordField?.closest("form") ||
    passwordField?.getRootNode() || document;
  const candidates = collectInputs(formRoot)
    .filter((input) => usable(input) && input !== passwordField &&
      !isPasswordField(input) && !isTotpField(input) && isTextLike(input));
  const usernameField = candidates.find((input) =>
    (input.autocomplete || "").toLowerCase().split(/\s+/).includes("username")) ||
    candidates.filter((input) => passwordField &&
      (input.compareDocumentPosition(passwordField) & Node.DOCUMENT_POSITION_FOLLOWING)).at(-1) ||
    candidates.find((input) => /user|login|email|account/i.test(
      [input.name, input.id, input.placeholder, input.getAttribute("aria-label")].filter(Boolean).join(" "))) ||
    candidates.find((input) => (input.type || "").toLowerCase() === "email") ||
    candidates[0];

  if (action === "detect") {
    return { found: Boolean(passwordField || usernameField), domain: currentDomain };
  }

  if (action === "probe") {
    // Return only whether a password value exists; never return the value itself.
    return {
      found: Boolean(passwordField || usernameField),
      domain: currentDomain,
      hasPassword: Boolean(passwordField && passwordField.value)
    };
  }

  if (action === "read") {
    return {
      found: Boolean(passwordField || usernameField),
      domain: currentDomain,
      username: usernameField?.value || "",
      password: passwordField?.value || ""
    };
  }

  const setValue = (input, value) => {
    input.focus();
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    if (setter) setter.call(input, value);
    else input.value = value;
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
    input.blur();
  };

  if (action === "fillNewPassword") {
    const passwordInputs = inputs.filter((input) => {
      const autocomplete = (input.autocomplete || "").toLowerCase().split(/\\s+/);
      return ((input.type || "").toLowerCase() === "password" ||
        autocomplete.includes("new-password")) && usable(input);
    });
    if (passwordInputs.length === 0) return "no_password";

    const hints = (input) => [
      input.name, input.id, input.placeholder, input.getAttribute("aria-label"),
      input.getAttribute("data-testid")
    ].filter(Boolean).join(" ");
    const isCurrent = (input) =>
      (input.autocomplete || "").toLowerCase().includes("current-password") ||
      /current|old|existing/i.test(hints(input));

    let targets = passwordInputs.filter((input) =>
      (input.autocomplete || "").toLowerCase().split(/\\s+/).includes("new-password"));

    if (targets.length === 0 && passwordInputs.length >= 3) {
      targets = passwordInputs.slice(-2);
    } else if (targets.length === 0 && passwordInputs.length === 2 &&
        passwordInputs[0].form && passwordInputs[0].form === passwordInputs[1].form) {
      targets = isCurrent(passwordInputs[0]) ? [passwordInputs[1]] : passwordInputs;
    }

    if (targets.length === 0) {
      const hintedTarget = passwordInputs.find((input) =>
        /new.?password|password.?new|create.?password|register.?password|signup.?password/i.test(hints(input)));
      if (hintedTarget) targets = [hintedTarget];
    }

    if (targets.length === 0) {
      const form = passwordInputs[0].form;
      const searchRoot = form || passwordInputs[0].getRootNode();
      const submitLabels = Array.from(
        searchRoot.querySelectorAll('button, input[type="submit"], input[type="button"]')
      )
        .map((element) => [element.innerText, element.value, element.getAttribute("aria-label")].filter(Boolean).join(" "))
        .join(" ");
      const pageText = [
        document.title,
        document.body?.innerText?.slice(0, 6000) || "",
        submitLabels
      ].join(" ");
      const registrationPattern = /sign.?up|register|create.{0,12}account|new account|create.{0,12}password/i;
      const looksLikeRegistration = registrationPattern.test(pageText);
      const registrationSubmit = registrationPattern.test(submitLabels);
      const looksLikeLogin = /sign.?in|log.?in|forgot password|current password/i.test(pageText);

      if (passwordInputs.length === 1 &&
          (registrationSubmit || (looksLikeRegistration && !looksLikeLogin))) {
        targets = [passwordInputs[0]];
      }
    }

    if (targets.length === 0) return "no_password";
    for (const target of targets) setValue(target, password);
    return "filled";
  }
  const totpFields = inputs.filter((input) => isTotpField(input) &&
    (isTextLike(input) || (input.type || "").toLowerCase() === "number"));

  if (totpFields.length > 0) {
    if (totpCode) {
      const splitCodeFields = totpFields.length > 1 && totpFields.length === totpCode.length &&
        totpFields.every((input) => input.maxLength === 1);
      if (splitCodeFields) {
        totpFields.forEach((input, index) => setValue(input, totpCode[index]));
      } else {
        setValue(totpFields[0], totpCode);
      }
    } else if (!passwordField) {
      return "totp_unavailable";
    }
    if (!passwordField) return "totp_filled";
  }

  if (!passwordField) {
    const usernameCandidates = inputs.filter((input) => !isTotpField(input) && isTextLike(input));
    const usernameOnlyField = usernameCandidates.find((input) =>
      (input.autocomplete || "").toLowerCase().split(/\s+/).includes("username")) ||
      usernameCandidates.find((input) => /\b(user(name)?|login|email|account)\b/i.test(
        [input.name, input.id, input.placeholder, input.getAttribute("aria-label")].filter(Boolean).join(" "))) ||
      usernameCandidates.find((input) => (input.type || "").toLowerCase() === "email") ||
      usernameCandidates.find((input) => (input.type || "").toLowerCase() === "text") ||
      usernameCandidates[0];
    if (!usernameOnlyField || !username) return "no_password";
    setValue(usernameOnlyField, username);
    return "username_only";
  }

  if (usernameField && username) setValue(usernameField, username);
  setValue(passwordField, password);
  if (totpFields.length > 0) return totpCode ? "totp_filled" : "totp_unavailable";
  return usernameField && username ? "filled" : "no_username";
}
