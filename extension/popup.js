const status = document.getElementById("status");
const fillBtn = document.getElementById("fillBtn");

fillBtn.addEventListener("click", async () => {
  status.textContent = "Requesting credentials…";
  fillBtn.disabled = true;
  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id) {
      status.textContent = "No active tab.";
      return;
    }
    const resp = await chrome.tabs.sendMessage(tab.id, { type: "fill_credentials" });
    status.textContent = resp?.ok
      ? "Credentials filled (if fields were found)."
      : "No matching credentials or Fortiva is locked.";
  } catch (e) {
    status.textContent = "Could not reach page. Is Fortiva unlocked?";
  } finally {
    fillBtn.disabled = false;
  }
});
