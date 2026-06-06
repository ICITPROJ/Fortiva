const siteLine = document.getElementById("siteLine");
const connPill = document.getElementById("connPill");
const connText = document.getElementById("connText");
const fillBtn = document.getElementById("fillBtn");
const statusBox = document.getElementById("statusBox");
const matchList = document.getElementById("matchList");
const actionHint = document.getElementById("actionHint");

let tabContext = null;
let fortivaStatus = "setup";
let pendingMatches = [];
let selectedEntryId = null;
let activeFillNonce = null;

const MESSAGES = {
  ready: "Fortiva is ready. Open a login form, then click Fill.",
  locked: "Fortiva is locked. Unlock the app on this PC, then try again.",
  setup: "Fortiva is not connected. Open Fortiva, unlock your vault, then Settings → Browser extension → Connect browser.",
  unreachable:
    "Fortiva could not be reached. Make sure the app is open and unlocked, then run Connect browser in Settings.",
  noTab: "Open a website tab first, then click the Fortiva icon.",
  badTab: "Fortiva can only fill on normal website pages (http/https).",
  tabChanged:
    "This tab changed since you opened the popup. The site line was refreshed — review it, then click Fill again.",
  homograph:
    "This hostname uses unusual characters. Confirm you are on the real site before filling.",
  noMatch: (host) =>
    `No saved login for ${host}. Add one in Fortiva with this site’s URL, then try again.`,
  multiple: "More than one login matches this site. Pick one below, then click Fill.",
  filled: (title) => `Filled “${title}”. Submit the form when you are ready.`,
  fieldsNotEmpty:
    "Login fields already have text. Clear them first if you want Fortiva to fill.",
  noPasswordField: "No password field found on this page yet.",
  fillFailed: "Could not fill the login fields on this page.",
  cancelled: "Unlock was cancelled. Open Fortiva and unlock to continue.",
  working: "Working…",
  unlocking: "Fortiva is locked — check the Fortiva window to unlock.",
  staleNonce: "Fill request expired. Close and reopen the popup, then try again.",
};

function setStatus(text, tone = "loading") {
  statusBox.textContent = text;
  statusBox.className = tone;
}

function setConnection(status, label) {
  fortivaStatus = status;
  connPill.className = `status-pill ${status}`;
  connText.textContent = label;
  fillBtn.disabled = status !== "ready" || !tabContext?.isFillable;
}

function sendMessage(message) {
  return new Promise((resolve) => {
    chrome.runtime.sendMessage(message, resolve);
  });
}

function displayHost(host, url) {
  let origin = host;
  try {
    origin = new URL(url).origin;
  } catch {
    /* keep host */
  }

  const ascii = host.includes("xn--") ? host : host;
  const label = origin !== host ? `${origin} (${host})` : origin;
  return { label, ascii, suspicious: /[^\x00-\x7F]/.test(host) || host.includes("xn--") };
}

function fillCredentialsOnPage(username, password, expectedHost) {
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

  if (!passField) return { ok: false, reason: "no_password_field" };
  if (hasUserInput(passField) || (userField && hasUserInput(userField))) {
    return { ok: false, reason: "fields_not_empty" };
  }

  setValue(userField, username);
  const filledPass = setValue(passField, password);
  return { ok: filledPass };
}

async function getActiveTabContext() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id) return { ok: false, reason: "no_tab" };

  if (!tab.url || (!tab.url.startsWith("http://") && !tab.url.startsWith("https://"))) {
    return { ok: false, reason: "bad_tab", tab };
  }

  let host;
  try {
    host = new URL(tab.url).hostname.toLowerCase();
  } catch {
    return { ok: false, reason: "bad_tab", tab };
  }

  return { ok: true, tab, host, url: tab.url, isFillable: true };
}

function renderSiteLine(context) {
  const display = displayHost(context.host, context.url);
  siteLine.innerHTML = `This page: <strong>${escapeHtml(display.label)}</strong>`;
  if (display.suspicious) {
    setStatus(MESSAGES.homograph, "warn");
  }
}

function renderMatches(matches) {
  matchList.innerHTML = "";
  pendingMatches = matches || [];
  selectedEntryId = pendingMatches.length === 1 ? pendingMatches[0].id : null;

  if (pendingMatches.length <= 1) {
    matchList.classList.remove("visible");
    actionHint.textContent =
      "Click Fill when the username and password fields are visible. Fortiva never fills without your click.";
    return;
  }

  matchList.classList.add("visible");
  actionHint.textContent = MESSAGES.multiple;

  for (const match of pendingMatches) {
    const li = document.createElement("li");
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "match-item" + (match.id === selectedEntryId ? " selected" : "");
    btn.innerHTML =
      `<span class="match-title">${escapeHtml(match.title || "Untitled")}</span>` +
      `<span class="match-user">${escapeHtml(match.username || "No username")}</span>`;
    btn.addEventListener("click", () => {
      selectedEntryId = match.id;
      for (const el of matchList.querySelectorAll(".match-item")) {
        el.classList.toggle("selected", el === btn);
      }
    });
    li.appendChild(btn);
    matchList.appendChild(li);
  }
}

function escapeHtml(text) {
  return String(text)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

async function refreshConnection() {
  const ping = await sendMessage({ type: "ping" });
  if (ping?.ok && ping.status === "ready") {
    setConnection("ready", "Ready to fill");
    setStatus(MESSAGES.ready, "");
    return;
  }
  if (ping?.status === "locked") {
    setConnection("locked", "Vault locked");
    setStatus(MESSAGES.locked, "warn");
    return;
  }
  if (ping?.status === "setup_required") {
    setConnection("setup", "Not connected");
    setStatus(ping.message || MESSAGES.unreachable, "error");
    return;
  }
  setConnection("setup", "Not connected");
  setStatus(MESSAGES.setup, "error");
}

async function preloadMatches(context = tabContext) {
  if (!context?.ok || fortivaStatus !== "ready") return;

  renderSiteLine(context);

  const list = await sendMessage({
    type: "list_credentials",
    domain: context.host,
    url: context.url,
  });

  if (list?.error === "locked") {
    setConnection("locked", "Vault locked");
    setStatus(MESSAGES.locked, "warn");
    return;
  }

  if (list?.error === "setup_required") {
    setConnection("setup", "Not connected");
    setStatus(MESSAGES.setup, "error");
    return;
  }

  activeFillNonce = list?.fillNonce || null;
  const matches = list?.matches || [];
  renderMatches(matches);

  if (matches.length === 0) {
    setStatus(MESSAGES.noMatch(context.host), "warn");
  } else if (matches.length === 1) {
    setStatus(`Found “${matches[0].title || "saved login"}”. Click Fill when ready.`, "");
  } else {
    setStatus(MESSAGES.multiple, "");
  }
}

async function init() {
  tabContext = await getActiveTabContext();

  if (!tabContext.ok) {
    siteLine.textContent =
      tabContext.reason === "no_tab" ? "No page open" : "Cannot fill on this tab";
    setConnection("setup", "Unavailable");
    setStatus(
      tabContext.reason === "no_tab" ? MESSAGES.noTab : MESSAGES.badTab,
      "warn"
    );
    fillBtn.disabled = true;
    return;
  }

  renderSiteLine(tabContext);
  await refreshConnection();
  if (fortivaStatus === "ready") await preloadMatches(tabContext);
}

fillBtn.addEventListener("click", async () => {
  fillBtn.disabled = true;
  setStatus(MESSAGES.working, "loading");

  try {
    const freshContext = await getActiveTabContext();
    if (!freshContext.ok) {
      setStatus(freshContext.reason === "no_tab" ? MESSAGES.noTab : MESSAGES.badTab, "warn");
      return;
    }

    if (tabContext?.ok && freshContext.host !== tabContext.host) {
      tabContext = freshContext;
      activeFillNonce = null;
      renderSiteLine(tabContext);
      await preloadMatches(tabContext);
      setStatus(MESSAGES.tabChanged, "warn");
      return;
    }

    tabContext = freshContext;
    renderSiteLine(tabContext);

    if (fortivaStatus !== "ready") {
      await refreshConnection();
      if (fortivaStatus !== "ready") return;
    }

    if (!activeFillNonce) {
      await preloadMatches(tabContext);
    }

    if (pendingMatches.length > 1 && !selectedEntryId) {
      setStatus("Choose a saved login from the list first.", "warn");
      return;
    }

    if (!activeFillNonce) {
      setStatus(MESSAGES.staleNonce, "warn");
      return;
    }

    setStatus(MESSAGES.unlocking, "loading");

    const creds = await sendMessage({
      type: "get_credentials",
      domain: tabContext.host,
      url: tabContext.url,
      entryId: selectedEntryId || undefined,
      fillNonce: activeFillNonce,
    });

    activeFillNonce = null;

    if (creds?.error === "invalid_nonce") {
      await preloadMatches(tabContext);
      setStatus(MESSAGES.staleNonce, "warn");
      return;
    }
    if (creds?.error === "cancelled") {
      setStatus(MESSAGES.cancelled, "warn");
      return;
    }
    if (creds?.error === "locked") {
      setConnection("locked", "Vault locked");
      setStatus(MESSAGES.locked, "warn");
      return;
    }
    if (creds?.error === "setup_required") {
      setConnection("setup", "Not connected");
      setStatus(MESSAGES.setup, "error");
      return;
    }
    if (creds?.error === "multiple_matches" && creds.matches?.length) {
      renderMatches(creds.matches);
      setStatus(MESSAGES.multiple, "warn");
      return;
    }
    if (!creds?.found) {
      setStatus(MESSAGES.noMatch(tabContext.host), "warn");
      return;
    }

    const [{ result }] = await chrome.scripting.executeScript({
      target: { tabId: tabContext.tab.id },
      func: fillCredentialsOnPage,
      args: [creds.username || "", creds.password || "", tabContext.host],
    });

    if (result?.ok) {
      setStatus(MESSAGES.filled(creds.title || tabContext.host), "success");
      return;
    }
    if (result?.reason === "host_mismatch") {
      setStatus(MESSAGES.tabChanged, "warn");
      return;
    }
    if (result?.reason === "fields_not_empty") {
      setStatus(MESSAGES.fieldsNotEmpty, "warn");
      return;
    }
    if (result?.reason === "no_password_field") {
      setStatus(MESSAGES.noPasswordField, "warn");
      return;
    }
    setStatus(MESSAGES.fillFailed, "error");
  } catch {
    setStatus(MESSAGES.setup, "error");
    setConnection("setup", "Not connected");
  } finally {
    fillBtn.disabled = fortivaStatus === "ready" && tabContext?.isFillable;
  }
});

init();
