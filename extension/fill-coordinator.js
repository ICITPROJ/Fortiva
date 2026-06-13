/**
 * Isolated-world fill — credentials never enter the page JavaScript context (no MAIN-world postMessage).
 * Entire module is guarded — popup fallback inject must not register duplicate listeners.
 */
(function () {
  if (document.documentElement.dataset.fortivaFillCoordinator) return;
  document.documentElement.dataset.fortivaFillCoordinator = "1";

  function isVisible(el) {
    if (!el || el.disabled || el.readOnly) return false;
    const style = window.getComputedStyle(el);
    return style.visibility !== "hidden" && style.display !== "none";
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

  function hasUserInput(el) {
    return el && String(el.value || "").trim().length > 0;
  }

  function setValue(el, value) {
    if (!el || !value || hasUserInput(el)) return false;
    const proto = el instanceof HTMLInputElement ? HTMLInputElement.prototype : null;
    const descriptor = proto ? Object.getOwnPropertyDescriptor(proto, "value") : null;
    if (descriptor?.set) descriptor.set.call(el, value);
    else el.value = value;
    el.dispatchEvent(
      new InputEvent("input", { bubbles: true, inputType: "insertFromPaste", data: value })
    );
    el.dispatchEvent(new Event("change", { bubbles: true }));
    try {
      el.focus();
      el.dispatchEvent(new FocusEvent("blur", { bubbles: true }));
    } catch {
      /* optional for custom elements */
    }
    return true;
  }

  function fillCredentialsOnPage(username, password, expectedHost) {
    const pageHost = String(location.hostname || "").toLowerCase();
    if (expectedHost && pageHost !== String(expectedHost).toLowerCase()) {
      return { ok: false, reason: "host_mismatch" };
    }

    const userField = findVisible([
      'input[autocomplete="username"]',
      'input[type="email"]',
      'input[name*="user" i]',
      'input[name*="email" i]',
      'input[id*="user" i]',
      'input[id*="email" i]',
    ]);
    const passField = findVisible([
      'input[type="password"]',
      'input[autocomplete="current-password"]',
    ]);

    if (!passField) {
      if (userField && username && setValue(userField, username)) {
        return { ok: false, reason: "password_step_pending" };
      }
      return { ok: false, reason: "no_password_field" };
    }
    if (hasUserInput(passField) || (userField && hasUserInput(userField))) {
      return { ok: false, reason: "fields_not_empty" };
    }

    setValue(userField, username);
    const filledPass = setValue(passField, password);
    if (!filledPass) {
      return { ok: false, reason: password ? "fill_error" : "empty_password" };
    }
    return { ok: true };
  }

  chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
    if (msg?.type !== "fortiva-fill") return false;

    let result;
    try {
      result = fillCredentialsOnPage(msg.username || "", msg.password || "", msg.expectedHost || "");
    } catch {
      result = { ok: false, reason: "fill_error" };
    } finally {
      if (typeof msg.password === "string") msg.password = "";
    }

    sendResponse(result);
    return true;
  });
})();
