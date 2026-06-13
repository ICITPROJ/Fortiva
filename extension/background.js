const NATIVE_HOSTS = [
  "com.fortiva.browserbridge.personal",
  "com.fortiva.browserbridge.enterprise",
];

function sendNativeMessage(message, callback, index = 0) {
  if (index >= NATIVE_HOSTS.length) {
    callback(null);
    return;
  }
  chrome.runtime.sendNativeMessage(NATIVE_HOSTS[index], message, (response) => {
    if (chrome.runtime.lastError) {
      sendNativeMessage(message, callback, index + 1);
      return;
    }
    callback(response || null);
  });
}

const NATIVE_TIMEOUT_MS = 20000;
const CREDENTIAL_TIMEOUT_MS = 130000;

async function nativeCredentialRequestWithRetry(envelope, attempts = 3) {
  let last = null;
  for (let i = 0; i < attempts; i++) {
    last = await nativeRequest(envelope, CREDENTIAL_TIMEOUT_MS);
    if (!last?.error || last.error !== "setup_required") return last;
    if (i < attempts - 1) await new Promise((r) => setTimeout(r, 600 * (i + 1)));
  }
  return last;
}

async function nativeRequestWithRetry(message, attempts = 4) {
  let last = null;
  for (let i = 0; i < attempts; i++) {
    last = await nativeRequest(message);
    if (last?.ok || last?.status === "locked" || last?.status === "setup_required")
      return last;
    if (i < attempts - 1) {
      const delay = last?.status === "bridge_warming" ? 550 * (i + 1) : 350;
      await new Promise((r) => setTimeout(r, delay));
    }
  }
  return last;
}

function nativeRequest(message, timeoutMs = NATIVE_TIMEOUT_MS) {
  return new Promise((resolve) => {
    let settled = false;
    const timer = setTimeout(() => {
      if (settled) return;
      settled = true;
      resolve({
        ok: false,
        status: "setup_required",
        message:
          "Fortiva did not respond in time. Click Fill again — Fortiva will connect and ask you to unlock.",
      });
    }, timeoutMs);

    sendNativeMessage(message, (response) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      resolve(response);
    });
  });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message?.type) {
    sendResponse(null);
    return;
  }

  if (sender.id !== chrome.runtime.id) {
    sendResponse(null);
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

  // Fill flow uses prepare_fill + execute_fill only (popup.js).

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
