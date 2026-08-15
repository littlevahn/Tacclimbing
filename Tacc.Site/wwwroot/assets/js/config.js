(() => {
  const localDevelopmentHosts = new Set(['localhost', '127.0.0.1']);
  const isLocalDevelopment = localDevelopmentHosts.has(window.location.hostname);
  const isDirectFilePreview = window.location.protocol === 'file:';

  window.TACC_CONFIG = Object.freeze({
    // Local static sites use the Functions Core Tools default port automatically.
    // For production on a separate Function App, replace the empty value with its public HTTPS origin.
    // Never add credentials or secrets to this public file.
    apiBaseUrl: isDirectFilePreview
      ? 'http://localhost:7071'
      : isLocalDevelopment
        ? `http://${window.location.hostname}:7071`
        : ''
  });
})();
