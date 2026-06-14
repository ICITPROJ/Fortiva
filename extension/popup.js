const siteLine = document.getElementById("siteLine");
const connPill = document.getElementById("connPill");
const connText = document.getElementById("connText");
const fillBtn = document.getElementById("fillBtn");
const statusBox = document.getElementById("statusBox");
const matchList = document.getElementById("matchList");
const actionHint = document.getElementById("actionHint");
const retryBtn = document.getElementById("retryBtn");
const previewCard = document.getElementById("previewCard");
const previewTitle = document.getElementById("previewTitle");
const previewUser = document.getElementById("previewUser");
const stepConnect = document.getElementById("stepConnect");
const stepUnlock = document.getElementById("stepUnlock");
const stepFill = document.getElementById("stepFill");
const versionLine = document.getElementById("versionLine");

let tabContext = null;
let fortivaStatus = "setup";
let pendingMatches = [];
let selectedEntryId = null;
let activeFillNonce = null;
let refreshInFlight = null;

const STATUS_TIMEOUT_MS = 8000;
const PERFORM_FILL_TIMEOUT_MS = 35000;

function connectionLabelForStatus(status) {
  switch (status) {
    case "ready":
      return "Ready to fill";
    case "locked":
      return "Vault locked";
    case "setup_required":
    default:
      return "Click Fill to connect";
  }
}

function connectionToneForStatus(status) {
  if (status === "ready") return "ready";
  if (status === "locked") return "locked";
  return "setup";
}

const MESSAGES = {
  ready: "Ready. One click on Fill completes single- and multi-step logins.",
  locked: "Unlock Fortiva on this PC, then click Fill again.",
  setup: "Open Fortiva from the Start menu and unlock your vault, then click Fill here.",
  launchFailed:
    "Fortiva could not start from the browser. Open Fortiva from the Start menu, unlock, then Settings → Connect browser.",
  unreachable:
    "Fortiva bridge is not connected. Open and unlock Fortiva, run Connect browser in Settings, then reload the extension.",
  noTab: "Open a website tab first, then click the Fortiva icon.",
  badTab: "Fortiva can only fill on normal website pages (http/https).",
  tabChanged:
    "This tab changed since you opened the popup. The site line was refreshed — review it, then click Fill again.",
  homograph:
    "This hostname uses punycode (xn--) or mixed-script characters. Confirm you are on the real site before filling.",
  noMatch: (host) =>
    `No saved login matched ${host}. In Fortiva, open the entry and set Website to https://${host} (or the full login page URL), then save.`,
  crossSubdomain:
    "Related logins exist for this domain but none match this exact page host. Open the site where the login was saved, or update the entry Website URL.",
  fillBlockedSuspicious:
    "Fill is disabled on this hostname (punycode or mixed-script). Confirm you are on the real site, then navigate to the correct URL.",
  invalidHost:
    "This page URL is not valid for autofill. Open a normal https login page, then try Fill again.",
  bridgeError:
    "Fortiva did not answer the browser bridge. Unlock Fortiva, run Connect browser in Settings, reload the extension, then try Fill again.",
  multiple: "More than one login matches this site. Pick one below, then click Fill.",
  filled: (title) => `Filled “${title}”. Submit the form when you are ready.`,
  fieldsNotEmpty:
    "Login fields already have text. Clear them first if you want Fortiva to fill.",
  noPasswordField: "No password field found on this page yet.",
  passwordStepPending:
    "Username filled and the next step was submitted. Fortiva is waiting to fill the password…",
  passwordStepWatching:
    "Username submitted. Fortiva will fill the password when the next step appears — no second click needed.",
  multiStepFilled: (title) =>
    `Filled “${title}” through a multi-step login. Submit when you are ready.`,
  usernamePartial:
    "Password filled, but the username field was not detected on this page. Paste your username manually or click the username box and Fill again.",
  fillFailed: "Could not fill the login fields on this page.",
  cancelled: "Unlock was cancelled. Click Fill again when you're ready.",
  rateLimited:
    "Too many unlock attempts from the browser. Wait five minutes, then click Fill again.",
  working: "Working…",
  staleNonce: "Fill request expired. Close and reopen the popup, then try again.",
  matchFound: (count) =>
    count === 1 ? "1 match found" : `${count} matches found`,
};

function setStatus(text, tone = "loading") {
  statusBox.textContent = text;
  statusBox.className = tone;
}

function setConnection(status, label) {
  fortivaStatus = status;
  connPill.className = `status-pill ${status}`;
  connText.textContent = label;
  const canFill =
    tabContext?.isFillable &&
    !tabContext?.suspicious &&
    (status === "ready" || status === "locked" || status === "setup");
  fillBtn.disabled = !canFill;
  retryBtn.hidden = status !== "setup";
  renderSteps(status);
}

function renderSteps(status) {
  for (const el of [stepConnect, stepUnlock, stepFill]) {
    el.classList.remove("active", "done");
  }

  const connectDone = status !== "setup";
  const unlockDone = status === "ready";
  const fillActive = status === "ready";

  if (connectDone) stepConnect.classList.add("done");
  else stepConnect.classList.add("active");

  if (unlockDone) stepUnlock.classList.add("done");
  else if (connectDone) stepUnlock.classList.add("active");

  if (fillActive) stepFill.classList.add("active");
  else if (unlockDone) stepFill.classList.add("done");
}

function showMatchPreview(matches) {
  if (matches?.length === 1) {
    previewCard.classList.add("visible");
    previewTitle.textContent = matches[0].title || "Saved login";
    previewUser.textContent = matches[0].username || "No username";
    return;
  }
  previewCard.classList.remove("visible");
}

function sendMessage(message, timeoutMs = STATUS_TIMEOUT_MS) {
  return new Promise((resolve) => {
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      resolve({
        ok: false,
        status: "setup_required",
        message: MESSAGES.unreachable,
      });
    }, timeoutMs);

    chrome.runtime.sendMessage(message, (response) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      if (chrome.runtime.lastError) {
        resolve({
          ok: false,
          status: "setup_required",
          message: MESSAGES.unreachable,
        });
        return;
      }
      resolve(response);
    });
  });
}

function displayHost(host, url) {
  let origin = host;
  try {
    origin = new URL(url).origin;
  } catch {
    /* keep host */
  }

  let display = host;
  try {
    if (host.includes("xn--")) {
      display = new URL(`https://${host}/`).hostname;
    }
  } catch {
    /* keep host */
  }

  const label =
    display !== host && origin.includes(host)
      ? `${origin.replace(host, display)}`
      : origin !== host
        ? `${origin} (${display})`
        : origin;
  const suspicious =
    hasMixedScriptHomograph(display) || host.toLowerCase().includes("xn--");
  return { label, display, suspicious };
}

function hasMixedScriptHomograph(host) {
  for (const label of host.split(".")) {
    if (!label) continue;
    let hasLatin = false;
    let hasCyrillic = false;
    let hasGreek = false;
    for (const ch of label) {
      if (/[a-z]/.test(ch)) {
        hasLatin = true;
        continue;
      }
      if (/[0-9\-]/.test(ch)) continue;
      const code = ch.codePointAt(0) ?? 0;
      if (code >= 0x0400 && code <= 0x04ff) hasCyrillic = true;
      else if (code >= 0x0370 && code <= 0x03ff) hasGreek = true;
    }
    if ((hasLatin && hasCyrillic) || (hasLatin && hasGreek)) return true;
  }
  return false;
}

async function performFillOnActiveTab(context) {
  return sendMessage(
    {
      type: "perform_fill",
      tabId: context.tab.id,
      domain: context.host,
      url: context.url,
      entryId: selectedEntryId || undefined,
      fillNonce: activeFillNonce || undefined,
    },
    PERFORM_FILL_TIMEOUT_MS
  );
}

function applyFillResult(result, host, title) {
  if (result?.ok) {
    if (result.multiStep) {
      setStatus(MESSAGES.multiStepFilled(title || host), "success");
      return true;
    }
    if (result.partial && result.reason === "username_not_found") {
      setStatus(MESSAGES.usernamePartial, "warn");
      return true;
    }
    setStatus(MESSAGES.filled(title || host), "success");
    return true;
  }
  if (result?.reason === "host_mismatch") {
    setStatus(MESSAGES.tabChanged, "warn");
    return true;
  }
  if (result?.reason === "fields_not_empty") {
    setStatus(MESSAGES.fieldsNotEmpty, "warn");
    return true;
  }
  if (result?.reason === "password_step_watching") {
    setStatus(MESSAGES.passwordStepWatching, "success");
    return true;
  }
  if (result?.reason === "password_step_pending") {
    setStatus(MESSAGES.passwordStepPending, "warn");
    return true;
  }
  if (result?.reason === "no_password_field") {
    setStatus(MESSAGES.noPasswordField, "warn");
    return true;
  }
  setStatus(MESSAGES.fillFailed, "error");
  return true;
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

  const suspicious = displayHost(host, tab.url).suspicious;
  return { ok: true, tab, host, url: tab.url, isFillable: !suspicious, suspicious };
}

function renderSiteLine(context) {
  const display = displayHost(context.host, context.url);
  context.suspicious = display.suspicious;
  siteLine.innerHTML = `This page: <strong>${escapeHtml(display.label)}</strong>`;
  if (display.suspicious) {
    setStatus(MESSAGES.fillBlockedSuspicious, "warn");
  }
}

function releasableMatches(matches) {
  return (matches || []).filter((m) => m.releasable !== false);
}

function renderMatches(matches) {
  matchList.innerHTML = "";
  const all = matches || [];
  pendingMatches = releasableMatches(all);
  selectedEntryId = pendingMatches.length === 1 ? pendingMatches[0].id : null;
  showMatchPreview(pendingMatches.length ? pendingMatches : all);

  if (pendingMatches.length === 0) {
    matchList.classList.remove("visible");
    actionHint.textContent =
      all.length > 0
        ? "Related logins were found for this domain but cannot be released on this exact host."
        : "Click Fill when the login fields are visible. Fortiva never fills without your click.";
    return;
  }

  if (pendingMatches.length <= 1) {
    matchList.classList.remove("visible");
    actionHint.textContent =
      pendingMatches.length === 1
        ? "Matched login found. Click Fill once — Fortiva handles multi-step logins automatically."
        : "Click Fill when the login fields are visible. Fortiva never fills without your click.";
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
      showMatchPreview([match]);
      for (const el of matchList.querySelectorAll(".match-item")) {
        el.classList.toggle("selected", el === btn);
      }
    });
    li.appendChild(btn);
    matchList.appendChild(li);
  }
}

function handleListError(list, host) {
  if (!list) {
    setConnection("setup", "Not connected");
    setStatus(MESSAGES.bridgeError, "error");
    return true;
  }

  const err = list.error;
  if (!err || err === "no_match") return false;

  if (err === "cancelled") {
    setStatus(MESSAGES.cancelled, "warn");
    return true;
  }
  if (err === "rate_limited") {
    setStatus(list.message || MESSAGES.rateLimited, "warn");
    return true;
  }
  if (err === "locked" || err === "vault_locked") {
    setConnection("locked", "Vault locked");
    setStatus(list.message || MESSAGES.locked, "warn");
    return true;
  }
  if (err === "setup_required" || err === "host_unreachable" || err === "token_stale" || err === "unknown_command") {
    setConnection("setup", "Not connected");
    setStatus(list.message || MESSAGES.setup, "error");
    return true;
  }
  if (err === "host_mismatch") {
    setStatus(MESSAGES.tabChanged, "warn");
    return true;
  }
  if (err === "invalid_host") {
    setStatus(MESSAGES.invalidHost, "warn");
    return true;
  }

  setConnection("setup", "Not connected");
  setStatus(MESSAGES.bridgeError, "error");
  return true;
}

function applyStatusResponse(response, host) {
  if (!response) {
    setConnection("setup", "Not connected");
    setStatus(MESSAGES.bridgeError, "error");
    return "setup";
  }

  const status = response.status || (response.ok ? "ready" : "setup_required");

  if (response.error === "locked" || response.error === "vault_locked" || status === "locked") {
    setConnection("locked", "Vault locked");
    setStatus(response.message || MESSAGES.locked, "warn");
    return "locked";
  }

  if (
    response.error === "setup_required" ||
    response.error === "host_unreachable" ||
    response.error === "token_stale" ||
    response.error === "internal_error"
  ) {
    setConnection("setup", "Click Fill to connect");
    setStatus(response.message || MESSAGES.setup, "error");
    return "setup";
  }

  if (status === "ready") {
    setConnection("ready", "Ready to fill");
    activeFillNonce = response.fillNonce || null;
    const matches = response.matches || [];
    const fillable = releasableMatches(matches);
    renderMatches(matches);
    if (fillable.length === 0 && matches.length > 0) {
      setStatus(MESSAGES.crossSubdomain, "warn");
    } else if (fillable.length === 0) {
      setStatus(MESSAGES.noMatch(host), "warn");
    } else if (fillable.length === 1) {
      setStatus(MESSAGES.matchFound(1), "success");
    } else {
      setStatus(MESSAGES.matchFound(fillable.length), "success");
    }
    return "ready";
  }

  setConnection("setup", "Click Fill to connect");
  setStatus(response.message || MESSAGES.setup, "warn");
  return "setup";
}

async function refreshStatus(context = tabContext) {
  if (!context?.ok) return "setup";
  if (refreshInFlight) return refreshInFlight;

  refreshInFlight = (async () => {
    try {
      setConnection("loading", "Checking…");
      setStatus("Checking Fortiva on this PC…", "loading");

      const response = await sendMessage(
        {
          type: "get_status_and_matches",
          domain: context.host,
          url: context.url,
        },
        STATUS_TIMEOUT_MS
      );

      return applyStatusResponse(response, context.host);
    } finally {
      refreshInFlight = null;
    }
  })();

  return refreshInFlight;
}

async function init() {
  try {
    const manifest = chrome.runtime.getManifest();
    versionLine.textContent = manifest?.version ? `v${manifest.version}` : "";
  } catch {
    versionLine.textContent = "";
  }

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
  await refreshStatus(tabContext);
}

retryBtn.addEventListener("click", async () => {
  retryBtn.disabled = true;
  try {
    await refreshStatus(tabContext);
  } finally {
    retryBtn.disabled = false;
  }
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !fillBtn.disabled) fillBtn.click();
});

function escapeHtml(text) {
  return String(text)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
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
        await refreshStatus(tabContext);
        setStatus(MESSAGES.tabChanged, "warn");
        return;
      }

      tabContext = freshContext;
      renderSiteLine(tabContext);

      if (tabContext.suspicious) {
        setStatus(MESSAGES.fillBlockedSuspicious, "warn");
        return;
      }

      if (fortivaStatus !== "ready") {
        const state = await refreshStatus(tabContext);
        if (state !== "ready") {
          if (state === "locked") setStatus(MESSAGES.locked, "warn");
          return;
        }
      }

      if (pendingMatches.length === 0) {
        await refreshStatus(tabContext);
        if (pendingMatches.length === 0) {
          setStatus(MESSAGES.noMatch(tabContext.host), "warn");
          return;
        }
      }

      if (pendingMatches.length > 1 && !selectedEntryId) {
        setStatus("Choose a saved login from the list first.", "warn");
        return;
      }

      setStatus("Filling login…", "loading");
      const response = await performFillOnActiveTab(tabContext);
      const creds = response?.creds;
      const fill = response?.fill;

      if (!response || !creds) {
        setConnection("setup", "Not connected");
        setStatus(MESSAGES.bridgeError, "error");
        return;
      }
      if (handleListError(creds, tabContext.host)) return;

      if (creds.matches?.length > 1 && !creds.found) {
        applyStatusResponse(
          { status: "ready", matches: creds.matches, fillNonce: creds.fillNonce },
          tabContext.host
        );
        setStatus(MESSAGES.multiple, "warn");
        return;
      }

      if (creds.error === "invalid_nonce") {
        activeFillNonce = null;
        await refreshStatus(tabContext);
        setStatus(MESSAGES.staleNonce, "warn");
        return;
      }
      if (creds?.error === "rate_limited") {
        setStatus(creds.message || MESSAGES.rateLimited, "warn");
        return;
      }
      if (creds?.error === "locked" || creds?.error === "vault_locked") {
        setConnection("locked", "Vault locked");
        setStatus(creds.message || MESSAGES.locked, "warn");
        return;
      }
      if (creds?.error === "setup_required" || creds?.error === "host_unreachable") {
        setConnection("setup", "Not connected");
        setStatus(creds.message || MESSAGES.launchFailed, "error");
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

      applyFillResult(fill, tabContext.host, creds.title);
      if (fill?.ok || fill?.reason === "password_step_watching") activeFillNonce = null;
  } catch {
    setStatus(MESSAGES.fillFailed, "error");
  } finally {
    const canFill =
      tabContext?.isFillable &&
      !tabContext?.suspicious &&
      (fortivaStatus === "ready" || fortivaStatus === "locked" || fortivaStatus === "setup");
    fillBtn.disabled = !canFill;
  }
});

init();
