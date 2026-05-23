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
    callback(response || { found: false });
  });
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type !== "get_credentials") {
    sendResponse({ found: false });
    return;
  }
  const domain = message.domain || "";
  sendNativeMessage(
    { command: "get_credentials", payload: { domain, url: message.url } },
    (response) => sendResponse(response || { found: false })
  );
  return true;
});
