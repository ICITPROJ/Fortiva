const NATIVE_HOST = "com.fortiva.browserbridge.personal";
const BRIDGE_HTTP = "http://127.0.0.1:7847";
const HTTP_TIMEOUT_MS = 3000;
const NATIVE_TIMEOUT_MS = 8000;
const EXECUTE_FILL_TIMEOUT_MS = 30000;

let cachedBridgeToken = null;

function emptyStatus(error = "host_unreachable", message) {
  return {
    status: { appRunning: false, vaultUnlocked: false, error },
    matches: [],
    nativeError: message || null,
  };
}

async function httpBridgeFetch(path, options = {}) {
  const headers = { Accept: "application/json", ...(options.headers || {}) };
  if (cachedBridgeToken) headers["X-Fortiva-Bridge-Token"] = cachedBridgeToken;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), options.timeoutMs ?? HTTP_TIMEOUT_MS);
  try {
    const response = await fetch(`${BRIDGE_HTTP}${path}`, {
      ...options,
      headers,
      signal: controller.signal,
    });
    if (!response.ok) return null;
    const data = await response.json();
    if (data?.bridgeToken) cachedBridgeToken = data.bridgeToken;
    return data;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

async function httpGetStatusAndMatches(domain, url) {
  const params = new URLSearchParams({ domain: domain || "", url: url || "" });
  return httpBridgeFetch(`/status-and-matches?${params}`, { method: "GET" });
}

async function httpExecuteFill(payload) {
  return httpBridgeFetch("/execute-fill", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
    timeoutMs: EXECUTE_FILL_TIMEOUT_MS,
  });
}

function sendNativeMessageWithTimeout(host, payload, timeoutMs) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("native_timeout")), timeoutMs);
    chrome.runtime
      .sendNativeMessage(host, payload)
      .then((response) => {
        clearTimeout(timer);
        resolve(response);
      })
      .catch((err) => {
        clearTimeout(timer);
        reject(err);
      });
  });
}

async function nativeCommand(payload, timeoutMs = NATIVE_TIMEOUT_MS) {
  try {
    const response = await sendNativeMessageWithTimeout(NATIVE_HOST, payload, timeoutMs);
    if (response && typeof response === "object") return response;
    return emptyStatus("host_unreachable", "Native host returned an empty response.");
  } catch (err) {
    const msg = err?.message || String(err);
    if (msg.includes("native_timeout")) {
      return emptyStatus(
        "host_unreachable",
        "Native host timed out. Open Fortiva, unlock, then run Connect browser in Settings."
      );
    }
    return emptyStatus(
      "host_unreachable",
      `Native messaging failed (${msg}). Run Connect browser in Fortiva Settings and reload the extension.`
    );
  }
}

async function getStatusAndMatches(domain, url) {
  const http = await httpGetStatusAndMatches(domain, url);
  if (http?.status) return http;
  return nativeCommand({
    command: "get_status_and_matches",
    payload: { domain: domain || "", url: url || "" },
  });
}

async function executeFill(payload) {
  const http = await httpExecuteFill(payload);
  if (http) return http;
  return nativeCommand({ command: "execute_fill", payload }, EXECUTE_FILL_TIMEOUT_MS);
}

function isVaultUnlocked(statusBlock) {
  return statusBlock?.vaultUnlocked === true || statusBlock?.vault_unlocked === true;
}

function mapStatusToLegacy(statusBlock, nativeError) {
  if (!statusBlock) {
    return {
      ok: false,
      status: "setup_required",
      message:
        nativeError ||
        "Fortiva bridge is not connected. Open Fortiva and unlock your vault.",
    };
  }

  const error = statusBlock.error;
  if (error === "vault_locked") {
    return {
      ok: false,
      status: "locked",
      message: "Fortiva is open but locked. Unlock in the Fortiva app, then try again.",
    };
  }
  if (error === "token_stale" || error === "host_unreachable") {
    return {
      ok: false,
      status: "setup_required",
      message:
        nativeError ||
        (error === "token_stale"
          ? "Fortiva session expired. Unlock Fortiva and click Fill again."
          : "Fortiva is not running. Open Fortiva from the Start menu and unlock your vault."),
    };
  }
  if (error === "internal_error") {
    return {
      ok: false,
      status: "setup_required",
      message: "Fortiva bridge error. Run Connect browser in Fortiva Settings.",
    };
  }

  if (isVaultUnlocked(statusBlock)) {
    return { ok: true, status: "ready" };
  }

  return { ok: false, status: "setup_required" };
}

function mapMatchesResponse(raw, domain) {
  const legacy = mapStatusToLegacy(raw?.status, raw?.nativeError);
  const matches = (raw?.matches || []).map((m) => ({
    id: m.id,
    title: m.title || m.url || domain || "Saved login",
    username: m.username || "",
    url: m.url || "",
    score: m.score ?? 0,
    releasable: m.releasable !== false,
  }));

  return {
    ...legacy,
    matches,
    fillNonce: raw?.fillNonce || null,
    error:
      legacy.status === "locked"
        ? "locked"
        : legacy.status === "setup_required"
          ? raw?.status?.error || "setup_required"
          : matches.length === 0
            ? "no_match"
            : undefined,
  };
}

function fillResultPriority(result) {
  if (!result) return -1;
  if (result.ok) return 100;
  switch (result.reason) {
    case "password_step_watching":
      return 90;
    case "password_step_pending":
      return 80;
    case "fields_not_empty":
      return 70;
    case "fill_error":
      return 60;
    case "no_password_field":
      return 50;
    case "host_mismatch":
      return 10;
    default:
      return 20;
  }
}

function isBetterFillResult(candidate, current) {
  return fillResultPriority(candidate) > fillResultPriority(current);
}

async function tryFillFrame(tabId, payload, frameId) {
  try {
    return await chrome.tabs.sendMessage(tabId, payload, { frameId });
  } catch {
    return null;
  }
}

async function collectFillResults(tabId, payload) {
  let best = null;
  const consider = (result) => {
    if (isBetterFillResult(result, best)) best = result;
  };

  consider(await tryFillFrame(tabId, payload, 0));

  const frames = await chrome.webNavigation.getAllFrames({ tabId });
  for (const frame of frames || []) {
    if (frame.frameId === 0) continue;
    consider(await tryFillFrame(tabId, payload, frame.frameId));
  }

  return best;
}

async function fillViaContentScript(tabId, username, password, expectedHost) {
  const payload = {
    type: "fortiva-fill",
    channel: crypto.randomUUID(),
    username: username || "",
    password: password || "",
    expectedHost: expectedHost || "",
  };

  let best = await collectFillResults(tabId, payload);
  if (best?.ok || best?.reason === "password_step_watching" || best?.reason === "password_step_pending") {
    payload.password = "";
    payload.username = "";
    return best;
  }

  await chrome.scripting.executeScript({
    target: { tabId, allFrames: true },
    files: ["fill-coordinator.js"],
    world: "ISOLATED",
  });

  best = await collectFillResults(tabId, payload);
  payload.password = "";
  payload.username = "";
  return best;
}

async function performFillOnTab(tabId, domain, url, entryId, fillNonce) {
  const creds = await executeFill({
    domain: domain || "",
    url,
    entryId: entryId || undefined,
    fillNonce: fillNonce || undefined,
  });

  if (!creds?.found) {
    return { creds, fill: null };
  }

  const fill = await fillViaContentScript(tabId, creds.username, creds.password, domain);
  return { creds, fill };
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message?.type || sender.id !== chrome.runtime.id) {
    sendResponse(null);
    return;
  }

  if (message.type === "get_status_and_matches") {
    getStatusAndMatches(message.domain, message.url).then((raw) =>
      sendResponse(mapMatchesResponse(raw, message.domain))
    );
    return true;
  }

  if (message.type === "execute_fill") {
    executeFill({
      domain: message.domain || "",
      url: message.url,
      entryId: message.entryId || undefined,
      fillNonce: message.fillNonce || undefined,
    }).then(sendResponse);
    return true;
  }

  if (message.type === "perform_fill") {
    performFillOnTab(
      message.tabId,
      message.domain,
      message.url,
      message.entryId,
      message.fillNonce
    ).then(sendResponse);
    return true;
  }

  sendResponse(null);
  return true;
});
