const NATIVE_HOST = "com.fortiva.browserbridge.personal";
const BRIDGE_HTTP = "http://127.0.0.1:7847";
const HTTP_TIMEOUT_MS = 3000;
const NATIVE_TIMEOUT_MS = 12000;
const TOKEN_FETCH_TIMEOUT_MS = 5000;
const EXECUTE_FILL_TIMEOUT_MS = 30000;

let cachedBridgeToken = null;

function extensionOrigin() {
  return `chrome-extension://${chrome.runtime.id}/`;
}

function emptyStatus(error = "host_unreachable", message) {
  return {
    status: { appRunning: false, vaultUnlocked: false, error },
    matches: [],
    nativeError: message || null,
  };
}

function hasBridgeToken(value) {
  return typeof value === "string" && value.length > 0;
}

async function httpBridgeFetch(path, options = {}) {
  const headers = {
    Accept: "application/json",
    Origin: extensionOrigin(),
    ...(options.headers || {}),
  };
  if (cachedBridgeToken) headers["X-Fortiva-Bridge-Token"] = cachedBridgeToken;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), options.timeoutMs ?? HTTP_TIMEOUT_MS);
  try {
    const response = await fetch(`${BRIDGE_HTTP}${path}`, {
      ...options,
      headers,
      signal: controller.signal,
    });
    if (!response.ok) {
      if (response.status === 401) cachedBridgeToken = null;
      return null;
    }
    const data = await response.json();
    if (hasBridgeToken(data?.bridgeToken)) cachedBridgeToken = data.bridgeToken;
    return data;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

async function httpBridgeFetchWithRetry(path, options = {}, attempts = 3) {
  for (let i = 0; i < attempts; i++) {
    const data = await httpBridgeFetch(path, options);
    if (data) return data;
    if (i < attempts - 1) {
      await new Promise((resolve) => setTimeout(resolve, 180 * (i + 1)));
    }
  }
  return null;
}

async function ensureBridgeToken(maxAttempts = 2) {
  if (cachedBridgeToken) return true;

  for (let i = 0; i < maxAttempts; i++) {
    const native = await nativeCommand({ command: "get_session_token" }, TOKEN_FETCH_TIMEOUT_MS);
    if (hasBridgeToken(native?.bridgeToken)) {
      cachedBridgeToken = native.bridgeToken;
      return true;
    }
    if (native?.status?.error === "vault_locked") return false;
    if (i < maxAttempts - 1) {
      await new Promise((resolve) => setTimeout(resolve, 150 * (i + 1)));
    }
  }

  return false;
}

async function getStatusAndMatches(domain, url) {
  const payload = { domain: domain || "", url: url || "" };

  // Native path is authoritative — does not require loopback HTTP token (avoids 8s+ token retry blocking status).
  const native = await nativeCommand({
    command: "get_status_and_matches",
    payload,
  });

  if (native?.status && isVaultUnlocked(native.status)) {
    void ensureBridgeToken(1);
    return native;
  }

  if (native?.status?.error === "vault_locked") {
    return native;
  }

  // Fast HTTP path when we already hold a token (e.g. repeat popup open).
  if (cachedBridgeToken) {
    const params = new URLSearchParams(payload);
    const http = await httpBridgeFetch(`/status-and-matches?${params}`, { method: "GET" });
    if (http?.status && isVaultUnlocked(http.status)) return http;
    cachedBridgeToken = null;
  }

  if (native?.status) return native;

  const params = new URLSearchParams(payload);
  const http = await httpBridgeFetchWithRetry(`/status-and-matches?${params}`, { method: "GET" }, 2);
  if (http?.status) return http;

  return emptyStatus();
}

async function executeFill(payload) {
  if (await ensureBridgeToken()) {
    const http = await httpExecuteFill(payload);
    if (http) return http;
    cachedBridgeToken = null;
  }
  return nativeCommand({ command: "execute_fill", payload }, EXECUTE_FILL_TIMEOUT_MS);
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
  if (error === "token_stale" || error === "host_unreachable" || error === "auth_required") {
    return {
      ok: false,
      status: "setup_required",
      message:
        nativeError ||
        (error === "token_stale"
          ? "Fortiva session expired. Unlock Fortiva and click Fill again."
          : error === "auth_required"
            ? "Fortiva needs a browser reconnect. Settings → Connect browser, then reload this extension."
            : "Fortiva is not running. Open Fortiva from the Start menu and unlock your vault."),
    };
  }
  if (error === "internal_error") {
    return {
      ok: false,
      status: "setup_required",
      message:
        "Fortiva bridge is still starting. Wait a moment, click Retry, or use Settings → Restart bridge.",
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
