const NATIVE_HOSTS = [
  "com.fortiva.browserbridge.personal",
  "com.fortiva.browserbridge.enterprise",
];

let bridgePort = null;
let connecting = false;
let reconnectTimer = null;
let currentBridgeSnapshot = {
  type: "STATE_CHANGED",
  state: "Uninitialized",
  isVaultUnlocked: false,
  ok: false,
  status: "setup_required",
};
let pendingRequest = null;

function snapshotToPingResponse(snapshot) {
  return {
    ok: snapshot.ok === true,
    status: snapshot.status || "setup_required",
    message: snapshot.message,
    state: snapshot.state,
    isVaultUnlocked: snapshot.isVaultUnlocked,
  };
}

function handleIncomingStatePush(message) {
  currentBridgeSnapshot = {
    type: "STATE_CHANGED",
    state: message.state || message.State || "Uninitialized",
    vaultExists: message.vaultExists ?? message.VaultExists,
    isVaultUnlocked: message.isVaultUnlocked ?? message.IsVaultUnlocked ?? false,
    cachedSessionToken: message.cachedSessionToken ?? message.CachedSessionToken,
    ok: message.ok ?? message.Ok ?? false,
    status: message.status ?? message.Status ?? "setup_required",
    message: message.message ?? message.Message,
    timestamp: message.timestamp ?? message.Timestamp,
  };

  try {
    chrome.runtime
      .sendMessage({ type: "bridge_state_updated", snapshot: currentBridgeSnapshot })
      .catch(() => {});
  } catch {
    /* popup may be closed */
  }
}

function onBridgePortMessage(message) {
  if (!message) return;
  const pushType = message.type || message.Type;
  if (pushType === "STATE_CHANGED" || message.state || message.State) {
    handleIncomingStatePush(message);
    return;
  }

  if (pendingRequest) {
    const { resolve, timer } = pendingRequest;
    pendingRequest = null;
    clearTimeout(timer);
    resolve(message);
  }
}

function scheduleReconnect(delayMs = 3000) {
  if (reconnectTimer !== null) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    establishAuthoritativeBridgeChannel(0);
  }, delayMs);
}

function teardownBridgePort() {
  if (reconnectTimer !== null) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  if (!bridgePort) return;
  try {
    bridgePort.onMessage.removeListener(onBridgePortMessage);
    bridgePort.onDisconnect.removeListener(onBridgePortDisconnected);
    bridgePort.disconnect();
  } catch {
    /* port may already be gone */
  }
  bridgePort = null;
}

function onBridgePortDisconnected() {
  bridgePort = null;
  connecting = false;
  currentBridgeSnapshot = {
    type: "STATE_CHANGED",
    state: "Faulted",
    isVaultUnlocked: false,
    ok: false,
    status: "setup_required",
  };
  if (pendingRequest) {
    const { resolve, timer } = pendingRequest;
    pendingRequest = null;
    clearTimeout(timer);
    resolve({
      ok: false,
      status: "setup_required",
      message: "Fortiva bridge disconnected. Reconnecting…",
    });
  }
  scheduleReconnect(3000);
}

function establishAuthoritativeBridgeChannel(hostIndex = 0) {
  if (bridgePort !== null || connecting) return;
  if (hostIndex >= NATIVE_HOSTS.length) {
    scheduleReconnect(5000);
    return;
  }

  connecting = true;
  teardownBridgePort();

  try {
    bridgePort = chrome.runtime.connectNative(NATIVE_HOSTS[hostIndex]);
  } catch {
    connecting = false;
    bridgePort = null;
    establishAuthoritativeBridgeChannel(hostIndex + 1);
    return;
  }

  bridgePort.onMessage.addListener(onBridgePortMessage);
  bridgePort.onDisconnect.addListener(onBridgePortDisconnected);
  connecting = false;
}

function ensureBridgePort() {
  if (bridgePort === null && !connecting) {
    establishAuthoritativeBridgeChannel(0);
  }
}

function nativeRequest(message, timeoutMs = 20000) {
  return new Promise((resolve) => {
    if (message?.command === "ping") {
      if (currentBridgeSnapshot.status && currentBridgeSnapshot.status !== "setup_required") {
        resolve(snapshotToPingResponse(currentBridgeSnapshot));
        return;
      }
    }

    ensureBridgePort();
    if (!bridgePort) {
      resolve({
        ok: false,
        status: "setup_required",
        message: "Fortiva bridge is not connected. Open Fortiva and unlock, then try again.",
      });
      return;
    }

    if (pendingRequest) {
      resolve({
        ok: false,
        status: "bridge_warming",
        message: "Fortiva is busy with another request. Try again in a moment.",
      });
      return;
    }

    const timer = setTimeout(() => {
      if (!pendingRequest) return;
      pendingRequest = null;
      resolve({
        ok: false,
        status: "setup_required",
        message:
          "Fortiva did not respond in time. Click Fill again — Fortiva will connect and ask you to unlock.",
      });
    }, timeoutMs);

    pendingRequest = { resolve, timer };
    try {
      bridgePort.postMessage(message);
    } catch {
      clearTimeout(timer);
      pendingRequest = null;
      resolve({
        ok: false,
        status: "setup_required",
        message: "Could not reach Fortiva bridge host.",
      });
    }
  });
}

async function nativeRequestWithRetry(message, attempts = 2) {
  let last = null;
  for (let i = 0; i < attempts; i++) {
    last = await nativeRequest(message);
    if (last?.ok || last?.status === "locked" || last?.status === "setup_required") return last;
    if (i < attempts - 1) {
      const delay = last?.status === "bridge_warming" ? 400 : 250;
      await new Promise((r) => setTimeout(r, delay));
    }
  }
  return last;
}

async function nativeCredentialRequestWithRetry(envelope, attempts = 3) {
  let last = null;
  for (let i = 0; i < attempts; i++) {
    last = await nativeRequest(envelope, 130000);
    if (!last?.error || last.error !== "setup_required") return last;
    if (i < attempts - 1) await new Promise((r) => setTimeout(r, 600 * (i + 1)));
  }
  return last;
}

chrome.runtime.onStartup.addListener(() => establishAuthoritativeBridgeChannel(0));
chrome.runtime.onInstalled.addListener(() => establishAuthoritativeBridgeChannel(0));
establishAuthoritativeBridgeChannel(0);

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message?.type) {
    sendResponse(null);
    return;
  }

  if (sender.id !== chrome.runtime.id) {
    sendResponse(null);
    return;
  }

  if (message.type === "get_bridge_snapshot") {
    sendResponse(snapshotToPingResponse(currentBridgeSnapshot));
    return;
  }

  if (message.type === "ping") {
    nativeRequestWithRetry({ command: "ping" }).then((response) =>
      sendResponse(response || { ok: false, status: "setup_required" })
    );
    return true;
  }

  if (message.type === "prepare_fill") {
    nativeCredentialRequestWithRetry(
      {
        command: "prepare_fill",
        payload: { domain: message.domain || "", url: message.url },
      },
      4
    ).then(sendResponse);
    return true;
  }

  if (message.type === "execute_fill") {
    nativeCredentialRequestWithRetry(
      {
        command: "execute_fill",
        payload: {
          domain: message.domain || "",
          url: message.url,
          entryId: message.entryId || undefined,
          fillNonce: message.fillNonce || undefined,
        },
      },
      2
    ).then(sendResponse);
    return true;
  }

  sendResponse(null);
  return true;
});
