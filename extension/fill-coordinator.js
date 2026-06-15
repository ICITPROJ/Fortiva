/**
 * Isolated-world fill — credentials never enter the page JavaScript context (no MAIN-world postMessage).
 * Supports multi-step logins (username first, password on next screen) with automatic advance + watch.
 */
(function () {
  const COORDINATOR_VERSION = "1.0.45";
  if (document.documentElement.dataset.fortivaFillCoordinator === COORDINATOR_VERSION) return;
  document.documentElement.dataset.fortivaFillCoordinator = COORDINATOR_VERSION;

  const USERNAME_SELECTORS = [
    'input[autocomplete="username"]',
    'input[autocomplete="email"]',
    'input[type="email"]',
    'input[type="text"][inputmode="email"]',
    'input[name*="user" i]',
    'input[name*="email" i]',
    'input[name*="login" i]',
    'input[name*="account" i]',
    'input[name*="identifier" i]',
    'input[name*="signin" i]',
    'input[name*="sign-in" i]',
    'input[name*="auth" i]',
    'input[name*="customer" i]',
    'input[name*="member" i]',
    'input[id*="user" i]',
    'input[id*="email" i]',
    'input[id*="login" i]',
    'input[id*="account" i]',
    'input[id*="identifier" i]',
    'input[aria-label*="email" i]',
    'input[aria-label*="user" i]',
    'input[aria-label*="login" i]',
    'input[placeholder*="email" i]',
    'input[placeholder*="user" i]',
    'input[placeholder*="login" i]',
  ];

  const PASSWORD_SELECTORS = [
    'input[type="password"]',
    'input[autocomplete="current-password"]',
    'input[autocomplete="new-password"]',
  ];

  const ADVANCE_BUTTON_TEXT =
    /^(next|continue|sign\s*in|log\s*in|login|submit|ok|weiter|fortfahren|anmelden|connexion|suivant|proceed|go)$/i;
  const ADVANCE_BUTTON_CONTAINS =
    /next|continue|sign\s*in|log\s*in|login|submit|weiter|fortfahren|anmelden|connexion|suivant|proceed/i;

  const MULTI_STEP_INITIAL_WAIT_MS = 8000;
  const MULTI_STEP_EXTENDED_WAIT_MS = 22000;

  let pendingFill = null;
  let passwordWatcher = null;

  function isVisible(el) {
    if (!el || el.disabled || el.readOnly) return false;
    if (el.type === "hidden" || el.type === "search") return false;
    if (el.getAttribute("aria-hidden") === "true") return false;
    if (el.tabIndex < 0 && el.type === "password") return false;
    if (el.classList?.contains("hidden")) return false;
    const style = window.getComputedStyle(el);
    if (style.visibility === "hidden" || style.display === "none" || style.opacity === "0") return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function queryDeep(selector, root = document) {
    const hits = [];
    for (const el of root.querySelectorAll(selector)) hits.push(el);
    for (const host of root.querySelectorAll("*")) {
      if (host.shadowRoot) hits.push(...queryDeep(selector, host.shadowRoot));
    }
    return hits;
  }

  function findVisible(selectors) {
    for (const sel of selectors) {
      for (const el of queryDeep(sel)) {
        if (isVisible(el)) return el;
      }
    }
    return null;
  }

  function isFillableTextInput(el) {
    if (!(el instanceof HTMLInputElement) || !isVisible(el)) return false;
    const type = (el.type || "text").toLowerCase();
    if (type === "password" || type === "hidden" || type === "search" || type === "checkbox" || type === "radio")
      return false;
    return type === "text" || type === "email" || type === "tel" || type === "url" || type === "";
  }

  function documentPosition(a, b) {
    if (a === b) return 0;
    const pos = a.compareDocumentPosition(b);
    if (pos & Node.DOCUMENT_POSITION_FOLLOWING) return -1;
    if (pos & Node.DOCUMENT_POSITION_PRECEDING) return 1;
    return 0;
  }

  /** True when the page is a password-only step (username was entered on a prior screen). */
  function isPasswordOnlyLoginStep(passField) {
    if (!passField) return false;

    const visibleUserField = findVisible(USERNAME_SELECTORS);
    if (visibleUserField) return false;

    const pathAndHash = `${location.pathname}${location.hash}${location.search}`.toLowerCase();
    if (
      /\/password|password-step|signin\/password|login\/password|auth\/password|verify-password|enter-password|step=password|step=2\b/i.test(
        pathAndHash
      )
    ) {
      return true;
    }

    const container = passField.closest("form, section, main, [role='main']") || document.body;
    const text = String(container?.innerText || "");
    const hasEmailOnPage = /[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,}/i.test(text);
    if (hasEmailOnPage) return true;

    return !findUsernameField(passField);
  }

  function findUsernameNearPassword(passField) {
    if (!passField) return null;

    const form = passField.closest("form");
    const scope = form || document;
    const candidates = queryDeep("input", scope).filter(isFillableTextInput);

    const beforePass = candidates
      .filter((el) => documentPosition(el, passField) < 0)
      .sort((a, b) => -documentPosition(a, b));

    if (beforePass.length > 0) return beforePass[0];

    for (const el of candidates) {
      if (el !== passField) return el;
    }
    return null;
  }

  function findUsernameField(passField) {
    const direct = findVisible(USERNAME_SELECTORS);
    if (direct) return direct;
    const nearPass = findUsernameNearPassword(passField);
    if (nearPass) return nearPass;
    if (!passField) {
      for (const el of queryDeep("input")) {
        if (isFillableTextInput(el)) return el;
      }
    }
    return null;
  }

  function hasUserInput(el) {
    return el && String(el.value || "").trim().length > 0;
  }

  function setValue(el, value) {
    if (!el || !value || hasUserInput(el)) return false;

    const stringValue = String(value);
    const proto =
      el instanceof HTMLInputElement
        ? HTMLInputElement.prototype
        : el instanceof HTMLTextAreaElement
          ? HTMLTextAreaElement.prototype
          : null;
    const descriptor = proto ? Object.getOwnPropertyDescriptor(proto, "value") : null;

    try {
      el.focus();
    } catch {
      /* custom elements */
    }

    if (descriptor?.set) descriptor.set.call(el, stringValue);
    else el.value = stringValue;

    for (const inputType of ["insertText", "insertFromPaste"]) {
      el.dispatchEvent(
        new InputEvent("input", { bubbles: true, cancelable: true, inputType, data: stringValue })
      );
    }
    el.dispatchEvent(new Event("change", { bubbles: true }));
    try {
      el.dispatchEvent(new FocusEvent("blur", { bubbles: true }));
    } catch {
      /* optional */
    }
    return true;
  }

  function buttonLabel(el) {
    return String(el.textContent || el.value || el.getAttribute("aria-label") || "").trim();
  }

  function findAdvanceButton(userField) {
    const form = userField?.closest("form");
    const scopes = form ? [form, document] : [document];

    for (const scope of scopes) {
      for (const sel of ['button[type="submit"]', 'input[type="submit"]']) {
        for (const el of queryDeep(sel, scope)) {
          if (isVisible(el)) return el;
        }
      }
    }

    for (const scope of scopes) {
      for (const el of queryDeep('button, input[type="button"], input[type="submit"], a[role="button"], [role="button"]', scope)) {
        if (!isVisible(el)) continue;
        const text = buttonLabel(el);
        if (!text) continue;
        if (ADVANCE_BUTTON_TEXT.test(text) || ADVANCE_BUTTON_CONTAINS.test(text)) return el;
      }
    }

    if (userField) {
      const form = userField.closest("form") || document;
      const buttons = queryDeep('button, input[type="submit"], [role="button"]', form).filter(isVisible);
      if (buttons.length === 1) return buttons[0];
    }

    return null;
  }

  function advanceLoginStep(userField) {
    const btn = findAdvanceButton(userField);
    if (btn) {
      btn.click();
      return true;
    }

    if (!userField) return false;

    const form = userField.closest("form");
    for (const type of ["keydown", "keypress", "keyup"]) {
      userField.dispatchEvent(
        new KeyboardEvent(type, { key: "Enter", code: "Enter", bubbles: true, cancelable: true })
      );
    }

    if (form) {
      try {
        if (typeof form.requestSubmit === "function") {
          form.requestSubmit();
          return true;
        }
      } catch {
        /* fall through */
      }
      try {
        form.submit();
        return true;
      } catch {
        /* optional */
      }
    }

    return false;
  }

  function findFillablePasswordField() {
    const passField = findVisible(PASSWORD_SELECTORS);
    if (!passField || hasUserInput(passField)) return null;
    return passField;
  }

  function waitForPasswordField(timeoutMs) {
    return new Promise((resolve) => {
      let settled = false;
      const finish = (field) => {
        if (settled) return;
        settled = true;
        observer.disconnect();
        clearInterval(interval);
        clearTimeout(timer);
        resolve(field);
      };

      const check = () => {
        const passField = findFillablePasswordField();
        if (passField) finish(passField);
      };

      check();
      const observer = new MutationObserver(check);
      observer.observe(document.documentElement, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["type", "class", "style", "hidden", "disabled"],
      });
      const interval = setInterval(check, 200);
      const timer = setTimeout(() => finish(null), timeoutMs);
    });
  }

  function clearPendingFill() {
    pendingFill = null;
    if (passwordWatcher) {
      passwordWatcher.disconnect();
      passwordWatcher = null;
    }
  }

  function scrubPendingCredentials() {
    if (!pendingFill) return;
    pendingFill.password = "";
    pendingFill.username = "";
  }

  function armPendingFill(username, password, expectedHost, ttlMs) {
    clearPendingFill();
    pendingFill = {
      username: username || "",
      password: password || "",
      expectedHost: expectedHost || "",
      expiresAt: Date.now() + ttlMs,
    };

    const tryComplete = () => {
      if (!pendingFill || Date.now() > pendingFill.expiresAt) {
        scrubPendingCredentials();
        clearPendingFill();
        return;
      }

      const pageHost = String(location.hostname || "").toLowerCase();
      if (pendingFill.expectedHost && pageHost !== String(pendingFill.expectedHost).toLowerCase()) {
        scrubPendingCredentials();
        clearPendingFill();
        return;
      }

      const passField = findFillablePasswordField();
      if (!passField || !pendingFill.password) return;

      if (setValue(passField, pendingFill.password)) {
        scrubPendingCredentials();
        clearPendingFill();
      }
    };

    passwordWatcher = new MutationObserver(tryComplete);
    passwordWatcher.observe(document.documentElement, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ["type", "class", "style", "hidden", "disabled"],
    });
    const interval = setInterval(() => {
      tryComplete();
      if (!pendingFill) clearInterval(interval);
      if (pendingFill && Date.now() > pendingFill.expiresAt) {
        scrubPendingCredentials();
        clearPendingFill();
        clearInterval(interval);
      }
    }, 250);
  }

  function fillCredentialsOnPage(username, password, expectedHost) {
    const pageHost = String(location.hostname || "").toLowerCase();
    if (expectedHost && pageHost !== String(expectedHost).toLowerCase()) {
      return { ok: false, reason: "host_mismatch" };
    }

    const passField = findVisible(PASSWORD_SELECTORS);
    const userField = findUsernameField(passField);

    if (!passField) {
      if (userField && username && setValue(userField, username)) {
        return { ok: false, reason: "password_step_pending", userFieldFound: true };
      }
      return { ok: false, reason: "no_password_field" };
    }

    if (hasUserInput(passField) || (userField && hasUserInput(userField))) {
      return { ok: false, reason: "fields_not_empty" };
    }

    const filledUser = username ? setValue(userField, username) : false;
    const filledPass = setValue(passField, password);

    if (!filledPass) {
      return { ok: false, reason: password ? "fill_error" : "empty_password" };
    }

    if (username && !filledUser) {
      if (isPasswordOnlyLoginStep(passField)) {
        clearPendingFill();
        return { ok: true, passwordOnlyStep: true };
      }
      return { ok: true, partial: true, reason: "username_not_found" };
    }

    clearPendingFill();
    return { ok: true };
  }

  async function completeMultiStepLogin(username, password, expectedHost) {
    const userField = findUsernameField(null);
    advanceLoginStep(userField);
    await new Promise((resolve) => setTimeout(resolve, 400));

    const passField = await waitForPasswordField(MULTI_STEP_INITIAL_WAIT_MS + MULTI_STEP_EXTENDED_WAIT_MS);
    if (passField && password && setValue(passField, password)) {
      clearPendingFill();
      return { ok: true, multiStep: true };
    }

    if (password) {
      armPendingFill(username, password, expectedHost, MULTI_STEP_EXTENDED_WAIT_MS);
      return {
        ok: false,
        reason: "password_step_watching",
        multiStep: true,
        detail: "Username submitted. Fortiva will fill the password when the next step appears.",
      };
    }

    return { ok: false, reason: "password_step_pending" };
  }

  async function fillCredentialsWithRecovery(username, password, expectedHost) {
    const initial = fillCredentialsOnPage(username, password, expectedHost);
    if (initial.ok || initial.reason !== "password_step_pending" || !password) {
      return initial;
    }

    return completeMultiStepLogin(username, password, expectedHost);
  }

  chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
    if (msg?.type !== "fortiva-fill") return false;

    (async () => {
      let result;
      try {
        result = await fillCredentialsWithRecovery(
          msg.username || "",
          msg.password || "",
          msg.expectedHost || ""
        );
      } catch {
        result = { ok: false, reason: "fill_error" };
      } finally {
        if (typeof msg.password === "string") msg.password = "";
        if (typeof msg.username === "string") msg.username = "";
      }

      sendResponse(result);
    })();

    return true;
  });
})();
