(() => {
  const localDevelopmentHosts = new Set(['localhost', '127.0.0.1']);
  const isLocalDevelopment = localDevelopmentHosts.has(window.location.hostname);
  const isDirectFilePreview = window.location.protocol === 'file:';

  // API bases include the Azure Functions route prefix.
  const productionApiBaseUrl = 'https://tacc-prod-rg-duaebkh7cwfef9e9.westus3-01.azurewebsites.net/api';

  const apiBaseUrl = isDirectFilePreview
    ? 'http://localhost:7071/api'
    : isLocalDevelopment
      ? `http://${window.location.hostname}:7071/api`
      : productionApiBaseUrl;

  window.TACC_CONFIG = Object.freeze({
    // Local static sites use the Functions Core Tools default port automatically.
    // Never add credentials, access tokens, or secrets to this public file.
    apiBaseUrl,
    admin: Object.freeze({
      productId: 'tacc-shirt'
    })
  });
})();
