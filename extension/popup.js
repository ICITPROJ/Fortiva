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

const MESSAGES = {
  ready: "Ready. Click Fill when the login fields are visible.",
  locked: "Click Fill — Fortiva will open and ask for Windows Hello or your master password.",
  setup: "Click Fill — Fortiva will open on this PC and ask you to unlock. You don't need the app open first.",
  launchFailed:
    "Fortiva could not start from the browser. Open Fortiva from the Start menu, unlock, then Settings → Connect browser.",
  bridge_warming:
    "Fortiva is unlocked — the bridge is starting. This usually takes a few seconds.",
  unreachable:
    "Click Fill below — Fortiva will connect and ask you to unlock. If this keeps failing, run Connect browser once in Fortiva Settings.",
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
    "Username filled. Continue to the password step on this site, then click Fill again.",
  fillFailed: "Could not fill the login fields on this page.",
  cancelled: "Unlock was cancelled. Click Fill again when you're ready.",
  rateLimited:
    "Too many unlock attempts from the browser. Wait five minutes, then click Fill again.",
  working: "Working…",
  unlocking:
    "Approve Windows Hello or enter your master password in Fortiva. Keep this popup open — Fill will finish automatically after unlock.",
  unlockFinishing: "Unlock received — finishing fill automatically…",
  openingFortiva: "Opening Fortiva on this PC… This can take up to 30 seconds the first time.",
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
  const canFill =
    tabContext?.isFillable &&
    !tabContext?.suspicious &&
    (status === "ready" ||
      status === "locked" ||
      status === "setup" ||
      status === "bridge_warming");
  fillBtn.disabled = !canFill;
  retryBtn.hidden = status !== "setup" && status !== "bridge_warming";
  renderSteps(status);
}

function renderSteps(status) {
  for (const el of [stepConnect, stepUnlock, stepFill]) {
    el.classList.remove("active", "done");
  }

  const connectDone = status !== "setup";
  const unlockDone = status === "ready" || status === "bridge_warming";
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

const MESSAGE_TIMEOUT_MS = 18000;
const UNLOCK_TIMEOUT_MS = 130000;

function sendMessage(message, timeoutMs = MESSAGE_TIMEOUT_MS) {
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

async function fillViaContentScript(tabId, username, password, expectedHost) {
  const payload = {
    type: "fortiva-fill",
    channel: crypto.randomUUID(),
    username: username || "",
    password: password || "",
    expectedHost: expectedHost || "",
  };

  try {
    return await chrome.tabs.sendMessage(tabId, payload);
  } catch {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ["fill-coordinator.js"],
      world: "ISOLATED",
    });
    return await chrome.tabs.sendMessage(tabId, payload);
  } finally {
    payload.password = "";
    payload.username = "";
  }
}

function applyFillResult(result, host, title) {
  if (result?.ok) {
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
        ? "Matched login found. Click Fill when the username and password fields are visible."
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
  if (err === "locked") {
    setConnection("locked", "Vault locked");
    setStatus(list.message || MESSAGES.locked, "warn");
    return true;
  }
  if (err === "setup_required" || err === "unknown_command") {
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

function escapeHtml(text) {
  return String(text)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function applyPrepareFillResponse(prep, host) {
  if (!prep) {
    setConnection("setup", "Not connected");
    setStatus(MESSAGES.bridgeError, "error");
    return "setup";
  }

  const status = prep.status || (prep.ok ? "ready" : "setup_required");
  if (status === "ready") {
    setConnection("ready", "Ready to fill");
    activeFillNonce = prep.fillNonce || null;
    const matches = prep.matches || [];
    const fillable = releasableMatches(matches);
    renderMatches(matches);
    if (fillable.length === 0 && matches.length > 0) {
      setStatus(MESSAGES.crossSubdomain, "warn");
    } else if (fillable.length === 0) {
      setStatus(MESSAGES.noMatch(host), "warn");
    } else if (fillable.length === 1) {
      setStatus(`Found “${fillable[0].title || "saved login"}”. Click Fill when ready.`, "success");
    } else {
      setStatus(MESSAGES.multiple, "");
    }
    return "ready";
  }
  if (status === "locked") {
    setConnection("locked", "Locked — click Fill");
    setStatus(MESSAGES.locked, "warn");
    return "locked";
  }
  if (status === "bridge_warming") {
    setConnection("bridge_warming", "Starting…");
    setStatus(prep.message || MESSAGES.bridge_warming, "loading");
    fillBtn.disabled = false;
    return "bridge_warming";
  }
  if (status === "setup_required") {
    setConnection("setup", "Click Fill to connect");
    setStatus(prep.message || MESSAGES.setup, "");
    return "setup";
  }
  setConnection("setup", "Click Fill to connect");
  setStatus(prep.message || MESSAGES.setup, "warn");
  return "setup";
}

async function refreshConnection() {
  if (!tabContext?.ok) return "setup";

  setConnection("loading", "Connecting…");
  setStatus("Checking Fortiva on this PC…", "loading");

  const prep = await sendMessage(
    {
      type: "prepare_fill",
      domain: tabContext.host,
      url: tabContext.url,
    },
    UNLOCK_TIMEOUT_MS
  );

  return applyPrepareFillResponse(prep, tabContext.host);
}

async function waitForReady(maxAttempts = 8) {
  for (let i = 0; i < maxAttempts; i++) {
    const state = await refreshConnection();
    if (state === "ready") return true;
    if (state !== "bridge_warming" && state !== "loading") return false;
    await new Promise((r) => setTimeout(r, 700 * (i + 1)));
  }
  return fortivaStatus === "ready";
}

async function preloadMatches(context = tabContext) {
  if (!context?.ok) return;
  renderSiteLine(context);
  if (fortivaStatus === "ready" && activeFillNonce) return;
  await refreshConnection();
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
  setConnection("loading", "Connecting…");
  setStatus("Checking Fortiva on this PC…", "loading");
  void (async () => {
    await refreshConnection();
    if (fortivaStatus === "bridge_warming") await waitForReady(6);
    if (fortivaStatus === "ready") await preloadMatches(tabContext);
    const canFill =
      tabContext?.isFillable &&
      !tabContext?.suspicious &&
      (fortivaStatus === "ready" ||
        fortivaStatus === "locked" ||
        fortivaStatus === "setup" ||
        fortivaStatus === "bridge_warming");
    fillBtn.disabled = !canFill;
  })();
}

retryBtn.addEventListener("click", async () => {
  retryBtn.disabled = true;
  try {
    await refreshConnection();
    if (fortivaStatus === "bridge_warming") await waitForReady(4);
    if (fortivaStatus === "ready" && tabContext?.ok) await preloadMatches(tabContext);
  } finally {
    retryBtn.disabled = false;
  }
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !fillBtn.disabled) fillBtn.click();
});

async function requestExecuteFill(context) {
  return sendMessage(
    {
      type: "execute_fill",
      domain: context.host,
      url: context.url,
      entryId: selectedEntryId || undefined,
      fillNonce: activeFillNonce || undefined,
    },
    UNLOCK_TIMEOUT_MS
  );
}

async function runFillWithAutoRetry(context) {
  let creds = await requestExecuteFill(context);

  if (creds?.error === "locked" || creds?.error === "cancelled") {
    setConnection("loading", "Finishing…");
    setStatus(MESSAGES.unlockFinishing, "loading");
    if (await waitForReady(20)) {
      await refreshConnection();
      creds = await requestExecuteFill(context);
    }
  }

  return creds;
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

    if (tabContext.suspicious) {
      setStatus(MESSAGES.fillBlockedSuspicious, "warn");
      return;
    }

    if (fortivaStatus === "ready") {
      setStatus(MESSAGES.working, "loading");
    } else if (fortivaStatus === "bridge_warming" || fortivaStatus === "loading") {
      setConnection("loading", "Connecting…");
      setStatus(MESSAGES.bridge_warming, "loading");
    } else if (fortivaStatus === "setup") {
      setConnection("loading", "Opening Fortiva…");
      setStatus(MESSAGES.openingFortiva, "loading");
    } else {
      setConnection("locked", "Unlocking…");
      setStatus(MESSAGES.unlocking, "loading");
    }

    if (fortivaStatus === "ready" && pendingMatches.length === 0) {
      setStatus(MESSAGES.crossSubdomain, "warn");
      return;
    }

    if (pendingMatches.length > 1 && !selectedEntryId) {
      setStatus("Choose a saved login from the list first.", "warn");
      return;
    }

    const creds = await runFillWithAutoRetry(tabContext);

    if (!creds) {
      setConnection("setup", "Not connected");
      setStatus(MESSAGES.bridgeError, "error");
      return;
    }
    if (handleListError(creds, tabContext.host)) return;

    if (creds.matches?.length > 1 && !creds.found) {
      applyPrepareFillResponse(
        { status: "ready", matches: creds.matches, fillNonce: creds.fillNonce },
        tabContext.host
      );
      setStatus(MESSAGES.multiple, "warn");
      return;
    }

    if (creds.error === "invalid_nonce") {
      await preloadMatches(tabContext);
      setStatus(MESSAGES.staleNonce, "warn");
      return;
    }
    if (creds?.error === "rate_limited") {
      setStatus(creds.message || MESSAGES.rateLimited, "warn");
      return;
    }
    if (creds?.error === "cancelled") {
      setStatus(MESSAGES.cancelled, "warn");
      return;
    }
    if (creds?.error === "locked") {
      setConnection("locked", "Vault locked");
      setStatus(creds.message || MESSAGES.locked, "warn");
      return;
    }
    if (creds?.error === "setup_required") {
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

    const result = await fillViaContentScript(
      tabContext.tab.id,
      creds.username,
      creds.password,
      tabContext.host
    );
    applyFillResult(result, tabContext.host, creds.title);
    if (result?.ok) activeFillNonce = null;
  } catch {
    setStatus(MESSAGES.fillFailed, "error");
  } finally {
    const canFill =
      tabContext?.isFillable &&
      !tabContext?.suspicious &&
      (fortivaStatus === "ready" ||
        fortivaStatus === "locked" ||
        fortivaStatus === "setup" ||
        fortivaStatus === "bridge_warming");
    fillBtn.disabled = !canFill;
  }
});

init();
