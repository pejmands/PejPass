(() => {
  const browserApi = globalThis.browser ?? globalThis.chrome;
  if (!browserApi) {
    throw new Error("PejPass requires a supported WebExtensions API.");
  }

  globalThis.pejpassBrowserApi = browserApi;
})();
