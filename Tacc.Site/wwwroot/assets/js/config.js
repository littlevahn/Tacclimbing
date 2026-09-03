(() => {
  const localDevelopmentHosts = new Set(['localhost', '127.0.0.1']);
  const isLocalDevelopment = localDevelopmentHosts.has(window.location.hostname);
  const isDirectFilePreview = window.location.protocol === 'file:';

  // TODO: Replace with the public HTTPS origin of the deployed Azure Function App.
  const productionApiBaseUrl = '';

  // These identifiers are public SPA configuration values, not secrets.
  // TODO: Replace with the TACC Admin Application (client) ID.
    const adminClientId = 'ba28fc48-45c2-4523-8895-e00e50506017';
  // TODO: Replace with the workforce tenant Directory (tenant) ID.
    const tenantId = 'bb894250-0a2c-4be1-b8d0-b38e83e5f2e3';
  // TODO: Replace TACC_API_CLIENT_ID with the TACC API Application (client) ID.
    const inventoryManageScope = 'api://33282eb4-338c-4bac-a4cd-276d3ab4a6f1/Inventory.Manage';

  const apiBaseUrl = isDirectFilePreview
    ? 'http://localhost:7071'
    : isLocalDevelopment
      ? `http://${window.location.hostname}:7071`
      : productionApiBaseUrl;

  window.TACC_CONFIG = Object.freeze({
    // Local static sites use the Functions Core Tools default port automatically.
    // Never add credentials, access tokens, or secrets to this public file.
    apiBaseUrl,
    admin: Object.freeze({
      clientId: adminClientId,
      tenantId,
      authority: `https://login.microsoftonline.com/${tenantId}`,
      apiScope: inventoryManageScope,
      productId: 'tacc-shirt'
    })
  });
})();
