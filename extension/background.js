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

function nativeRequest(message) {
  return new Promise((resolve) => {
    sendNativeMessage(message, (response) => resolve(response));
  });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message?.type) {
    sendResponse(null);
    return;
  }

  if (message.type === "ping") {
    nativeRequest({ command: "ping" }).then((response) =>
      sendResponse(response || { ok: false, status: "setup_required" })
    );
    return true;
  }

  if (message.type === "list_credentials") {
    nativeRequest({
      command: "list_credentials",
      payload: { domain: message.domain || "", url: message.url },
    }).then(sendResponse);
    return true;
  }

  if (message.type === "get_credentials") {
    nativeRequest({
      command: "get_credentials",
      payload: {
        domain: message.domain || "",
        url: message.url,
        entryId: message.entryId || undefined,
      },
    }).then(sendResponse);
    return true;
  }

  sendResponse(null);
  return true;
});
