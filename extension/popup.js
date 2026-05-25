const status = document.getElementById("status");
const fillBtn = document.getElementById("fillBtn");

/**
 * Injected into the active tab only when the user clicks Fill.
 * Must stay self-contained — Chrome serializes this function into the page.
 * No focus listeners, no overlays, no passive autofill.
 */
function fillCredentialsOnPage(username, password) {
  function isVisible(el) {
    if (!el || el.disabled || el.readOnly) return false;
    const style = window.getComputedStyle(el);
    return style.visibility !== "hidden" && style.display !== "none";
  }

  function findVisible(selectors) {
    for (const sel of selectors) {
      for (const el of document.querySelectorAll(sel)) {
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
    el.value = value;
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
    return true;
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
    return { ok: false, reason: "no_password_field" };
  }

  if (hasUserInput(passField) || (userField && hasUserInput(userField))) {
    return { ok: false, reason: "fields_not_empty" };
  }

  setValue(userField, username);
  const filledPass = setValue(passField, password);
  return { ok: filledPass };
}

function getCredentials(domain, url) {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage({ type: "get_credentials", domain, url }, resolve);
  });
}

fillBtn.addEventListener("click", async () => {
  status.textContent = "Connecting to Fortiva…";
  fillBtn.disabled = true;
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id) {
      status.textContent = "No active tab.";
      return;
    }
    if (!tab.url || (!tab.url.startsWith("http://") && !tab.url.startsWith("https://"))) {
      status.textContent = "Open a normal website tab first.";
      return;
    }

    let domain;
    let url;
    try {
      url = tab.url;
      domain = new URL(url).hostname;
    } catch {
      status.textContent = "Cannot fill on this page.";
      return;
    }

    status.textContent =
      "If Fortiva is locked, unlock it in the app window (password or Windows Hello)…";
    const creds = await getCredentials(domain, url);
    if (!creds?.found) {
      status.textContent =
        "No matching entry for this site, or unlock was cancelled. Check that Fortiva is unlocked and you have a login saved for this URL.";
      return;
    }

    const [{ result }] = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: fillCredentialsOnPage,
      args: [creds.username || "", creds.password || ""],
    });

    if (result?.ok) {
      status.textContent = "Credentials filled on this page.";
      return;
    }
    if (result?.reason === "fields_not_empty") {
      status.textContent =
        "Did not fill — the login fields already contain text. Clear them first if you want Fortiva to fill.";
      return;
    }
    if (result?.reason === "no_password_field") {
      status.textContent = "No password field found on this page.";
      return;
    }

    status.textContent = "Could not fill login fields on this page.";
  } catch {
    status.textContent =
      "Could not reach Fortiva. Open Fortiva → Settings → Browser extension, run Set up browser connection, unlock the vault, and try again.";
  } finally {
    fillBtn.disabled = false;
  }
});
