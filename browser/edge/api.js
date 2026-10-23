const protocolVersion = 1;

function sendNativeMessage(message) {
  return globalThis.pejpassBrowserApi.runtime
    .sendMessage({ target: "pejpass-native", payload: message })
    .then((response) => {
      if (!response || (response.type === "error" && response.code === "host_unavailable")) {
        throw new Error(response?.message || "Native host unavailable.");
      }
      return response;
    });
}

function isValidBaseResponse(response, type) {
  return response?.type === type && response?.app === "PejPass" &&
    response?.protocolVersion === protocolVersion;
}
