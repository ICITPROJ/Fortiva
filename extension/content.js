function getDomain() {
  try {
    return new URL(location.href).hostname;
  } catch {
    return "";
  }
}

function fillCredentials(username, password) {
  const userField =
    document.querySelector('input[type="email"]') ||
    document.querySelector('input[autocomplete="username"]') ||
    document.querySelector('input[name*="user" i]');
  const passField =
    document.querySelector('input[type="password"]') ||
    document.querySelector('input[autocomplete="current-password"]');
  if (userField && username) userField.value = username;
  if (passField && password) passField.value = password;
}

// Credentials are filled only on explicit user action (popup), never on passive page load.
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== "fill_credentials") return;
  chrome.runtime.sendMessage(
    { type: "get_credentials", domain: getDomain(), url: location.href },
    (resp) => {
      if (resp?.found) fillCredentials(resp.username, resp.password);
      sendResponse({ ok: !!resp?.found });
    }
  );
  return true;
});
