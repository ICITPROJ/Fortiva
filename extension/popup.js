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

const MESSAGES = {
  ready: "Fortiva is ready. Open a login form, then click Fill.",
  locked: "Fortiva is locked. Unlock the app on this PC, then try again.",
  setup: "Fortiva is not connected. Open Fortiva → Settings → Browser extension → Connect browser.",
  noTab: "Open a website tab first, then click the Fortiva icon.",
  badTab: "Fortiva can only fill on normal website pages (http/https).",
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
    host = new URL(tab.url).hostname;
  } catch {
    return { ok: false, reason: "bad_tab", tab };
  }

  return { ok: true, tab, host, url: tab.url, isFillable: true };
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
  setConnection("setup", "Not connected");
  setStatus(MESSAGES.setup, "error");
}

async function preloadMatches() {
  if (!tabContext?.ok || fortivaStatus !== "ready") return;

  siteLine.innerHTML = `This page: <strong>${escapeHtml(tabContext.host)}</strong>`;

  const list = await sendMessage({
    type: "list_credentials",
    domain: tabContext.host,
    url: tabContext.url,
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

  const matches = list?.matches || [];
  renderMatches(matches);

  if (matches.length === 0) {
    setStatus(MESSAGES.noMatch(tabContext.host), "warn");
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

  siteLine.innerHTML = `This page: <strong>${escapeHtml(tabContext.host)}</strong>`;
  await refreshConnection();
  if (fortivaStatus === "ready") await preloadMatches();
}

fillBtn.addEventListener("click", async () => {
  if (!tabContext?.ok) return;

  fillBtn.disabled = true;
  setStatus(MESSAGES.working, "loading");

  try {
    if (fortivaStatus !== "ready") {
      await refreshConnection();
      if (fortivaStatus !== "ready") return;
    }

    if (pendingMatches.length > 1 && !selectedEntryId) {
      setStatus("Choose a saved login from the list first.", "warn");
      return;
    }

    setStatus(MESSAGES.unlocking, "loading");

    const creds = await sendMessage({
      type: "get_credentials",
      domain: tabContext.host,
      url: tabContext.url,
      entryId: selectedEntryId || undefined,
    });

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
      args: [creds.username || "", creds.password || ""],
    });

    if (result?.ok) {
      setStatus(MESSAGES.filled(creds.title || tabContext.host), "success");
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
    fillBtn.disabled = fortivaStatus !== "ready";
  }
});

init();
