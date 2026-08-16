import {
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  LogLevel,
  PublicClientApplication
} from '@azure/msal-browser';

(() => {
  const REQUEST_TIMEOUT_MS = 10000;
  const MAX_QUANTITY = 2147483647;
  const VARIANT_LABELS = Object.freeze({
    S: 'Small',
    M: 'Medium',
    L: 'Large',
    XL: 'XL'
  });

  const views = new Map(
    [...document.querySelectorAll('[data-view]')]
      .map((element) => [element.dataset.view, element])
  );
  const signInButton = document.querySelector('[data-sign-in]');
  const signOutButtons = [...document.querySelectorAll('[data-sign-out]')];
  const currentUserElements = [...document.querySelectorAll('[data-current-user]')];
  const authError = document.querySelector('[data-auth-error]');
  const productIdElement = document.querySelector('[data-product-id]');
  const productNameElement = document.querySelector('[data-product-name]');
  const statusElement = document.querySelector('[data-inventory-status]');
  const inventoryForm = document.querySelector('[data-inventory-form]');
  const inventoryFieldset = document.querySelector('[data-inventory-fieldset]');
  const variantList = document.querySelector('[data-variant-list]');
  const saveButton = document.querySelector('[data-save]');
  const reloadButton = document.querySelector('[data-reload]');
  const conflictReloadButton = document.querySelector('[data-conflict-reload]');

  const state = {
    msalInstance: null,
    account: null,
    inventory: null,
    originalQuantities: new Map(),
    inputs: new Map(),
    dirty: false,
    saving: false
  };

  const setView = (viewName) => {
    views.forEach((view, name) => {
      view.hidden = name !== viewName;
    });
  };

  const setStatus = (message, type = 'neutral') => {
    statusElement.textContent = message;
    statusElement.classList.remove(
      'admin-status-success',
      'admin-status-error',
      'admin-status-conflict'
    );

    if (type !== 'neutral') {
      statusElement.classList.add(`admin-status-${type}`);
    }
  };

  const getAdminUrl = () => new URL('./', window.location.href).href;

  const hasPlaceholder = (value) =>
    typeof value !== 'string' ||
    value.trim() === '' ||
    /(?:TACC_ADMIN_CLIENT_ID|TACC_API_CLIENT_ID|TENANT_ID|AZURE_FUNCTION_API_BASE_URL)/.test(value);

  const readConfiguration = () => {
    const rootConfig = window.TACC_CONFIG;
    const adminConfig = rootConfig?.admin;
    const invalidFields = [];

    if (hasPlaceholder(rootConfig?.apiBaseUrl)) invalidFields.push('apiBaseUrl');
    if (hasPlaceholder(adminConfig?.clientId)) invalidFields.push('clientId');
    if (hasPlaceholder(adminConfig?.tenantId)) invalidFields.push('tenantId');
    if (hasPlaceholder(adminConfig?.authority)) invalidFields.push('authority');
    if (hasPlaceholder(adminConfig?.apiScope)) invalidFields.push('apiScope');
    if (hasPlaceholder(adminConfig?.productId)) invalidFields.push('productId');

    if (window.location.protocol === 'file:') {
      invalidFields.push('httpOrigin');
    }

    if (invalidFields.length > 0) {
      console.error('TACC admin configuration is incomplete.', invalidFields);
      return null;
    }

    try {
      const apiUrl = new URL(rootConfig.apiBaseUrl);
      const authorityUrl = new URL(adminConfig.authority);

      if (!['http:', 'https:'].includes(apiUrl.protocol) || authorityUrl.protocol !== 'https:') {
        throw new TypeError('Admin URLs use unsupported protocols.');
      }
    } catch (error) {
      console.error('TACC admin configuration contains an invalid URL.', error);
      return null;
    }

    return Object.freeze({
      apiBaseUrl: rootConfig.apiBaseUrl.trim().replace(/\/+$/, ''),
      clientId: adminConfig.clientId.trim(),
      tenantId: adminConfig.tenantId.trim(),
      authority: adminConfig.authority.trim().replace(/\/+$/, ''),
      apiScope: adminConfig.apiScope.trim(),
      productId: adminConfig.productId.trim(),
      redirectUri: getAdminUrl()
    });
  };

  const config = readConfiguration();

  const getAccountDisplayName = (account) =>
    account?.name?.trim() || account?.username?.trim() || 'Authenticated administrator';

  const showSignedOut = (failed = false) => {
    state.account = null;
    state.inventory = null;
    state.originalQuantities = new Map();
    state.inputs = new Map();
    state.dirty = false;
    authError.hidden = !failed;
    setView('signed-out');
  };

  const showUnauthorized = () => {
    const displayName = getAccountDisplayName(state.account);
    currentUserElements.forEach((element) => {
      element.textContent = displayName;
    });
    setView('unauthorized');
  };

  const showAuthorized = () => {
    const displayName = getAccountDisplayName(state.account);
    currentUserElements.forEach((element) => {
      element.textContent = displayName;
    });
    setView('authorized');
  };

  const setBusy = (busy, label = 'Save inventory') => {
    state.saving = busy;
    saveButton.disabled = busy;
    reloadButton.disabled = busy;
    conflictReloadButton.disabled = busy;
    inventoryFieldset.disabled = busy;
    saveButton.textContent = busy ? 'Saving\u2026' : label;
  };

  const validateQuantityInput = (input) => {
    const value = input.value.trim();
    const quantity = Number(value);
    const isValid = value !== '' &&
      Number.isSafeInteger(quantity) &&
      quantity >= 0 &&
      quantity <= MAX_QUANTITY;

    input.setCustomValidity(isValid ? '' : 'Enter a whole number of zero or greater.');
    return isValid;
  };

  const collectQuantities = () => {
    const quantities = {};
    let valid = true;

    state.inputs.forEach((input, variantId) => {
      if (!validateQuantityInput(input)) {
        valid = false;
        return;
      }

      quantities[variantId] = Number(input.value);
    });

    return valid ? quantities : null;
  };

  const updateDirtyState = () => {
    if (!state.inventory) {
      state.dirty = false;
      return;
    }

    state.dirty = [...state.inputs].some(([variantId, input]) => {
      const value = input.value.trim();
      const currentQuantity = Number(input.value);
      return value === '' || !Number.isSafeInteger(currentQuantity) ||
        currentQuantity !== state.originalQuantities.get(variantId);
    });

    if (state.dirty) {
      conflictReloadButton.hidden = true;
    }
  };

  const createVariantRow = (variant, index) => {
    const row = document.createElement('div');
    row.className = 'inventory-admin-row';

    const inputId = `inventory-variant-${index}`;
    const label = document.createElement('label');
    label.htmlFor = inputId;

    const displayName = document.createElement('strong');
    displayName.textContent = VARIANT_LABELS[variant.variantId] || variant.variantId;

    const variantCode = document.createElement('span');
    variantCode.textContent = `Variant ${variant.variantId}`;

    label.append(displayName, variantCode);

    const input = document.createElement('input');
    input.id = inputId;
    input.name = variant.variantId;
    input.type = 'number';
    input.min = '0';
    input.max = String(MAX_QUANTITY);
    input.step = '1';
    input.required = true;
    input.inputMode = 'numeric';
    input.autocomplete = 'off';
    input.value = String(variant.quantity);
    input.setAttribute('aria-label', `${displayName.textContent} inventory quantity`);
    input.addEventListener('input', () => {
      validateQuantityInput(input);
      updateDirtyState();
    });

    row.append(label, input);
    state.inputs.set(variant.variantId, input);
    return row;
  };

  const parseInventoryResponse = (payload) => {
    if (!payload || typeof payload !== 'object' ||
      typeof payload.productId !== 'string' || payload.productId.trim() === '' ||
      typeof payload.name !== 'string' || payload.name.trim() === '' ||
      typeof payload.etag !== 'string' || !/^".+"$/.test(payload.etag) ||
      !Array.isArray(payload.variants) || payload.variants.length === 0) {
      throw new TypeError('The admin inventory response has an unexpected product structure.');
    }

    const seenVariantIds = new Set();
    const variants = payload.variants.map((variant) => {
      if (!variant || typeof variant !== 'object' ||
        typeof variant.variantId !== 'string' || variant.variantId.trim() === '' ||
        seenVariantIds.has(variant.variantId) ||
        !Number.isSafeInteger(variant.quantity) || variant.quantity < 0) {
        throw new TypeError('The admin inventory response contains a malformed variant.');
      }

      seenVariantIds.add(variant.variantId);
      return Object.freeze({
        variantId: variant.variantId,
        quantity: variant.quantity
      });
    });

    return Object.freeze({
      productId: payload.productId,
      name: payload.name,
      etag: payload.etag,
      variants
    });
  };

  const renderInventory = (inventory) => {
    state.inventory = inventory;
    state.inputs = new Map();
    state.originalQuantities = new Map(
      inventory.variants.map((variant) => [variant.variantId, variant.quantity])
    );
    state.dirty = false;

    productIdElement.textContent = `PRODUCT / ${inventory.productId}`;
    productNameElement.textContent = inventory.name;
    variantList.replaceChildren(
      ...inventory.variants.map((variant, index) => createVariantRow(variant, index))
    );
    conflictReloadButton.hidden = true;
    inventoryForm.hidden = false;
  };

  const getAccessToken = async (forceRefresh = false) => {
    try {
      const response = await state.msalInstance.acquireTokenSilent({
        account: state.account,
        scopes: [config.apiScope],
        forceRefresh
      });
      return response.accessToken;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError || error?.errorCode === 'interaction_required') {
        await state.msalInstance.acquireTokenRedirect({
          account: state.account,
          scopes: [config.apiScope],
          redirectUri: config.redirectUri,
          redirectStartPage: config.redirectUri
        });
        return null;
      }

      console.error('TACC admin token acquisition failed.', error);
      throw error;
    }
  };

  const fetchWithTimeout = async (url, options) => {
    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

    try {
      return await fetch(url, { ...options, signal: controller.signal });
    } finally {
      window.clearTimeout(timeoutId);
    }
  };

  const authorizedFetch = async (url, options = {}) => {
    const send = async (forceRefresh) => {
      const accessToken = await getAccessToken(forceRefresh);
      if (!accessToken) {
        return null;
      }

      return fetchWithTimeout(url, {
        ...options,
        headers: {
          Accept: 'application/json',
          ...options.headers,
          Authorization: `Bearer ${accessToken}`
        },
        cache: 'no-store'
      });
    };

    let response = await send(false);
    if (response?.status === 401) {
      response = await send(true);
    }

    return response;
  };

  const handleApiError = (response, operation) => {
    switch (response?.status) {
      case 400:
        setStatus('One or more inventory quantities are invalid.', 'error');
        break;
      case 401:
        console.error(`TACC admin ${operation} remained unauthorized after token reacquisition.`);
        showSignedOut(true);
        break;
      case 403:
        showUnauthorized();
        break;
      case 404:
        setStatus('The requested inventory product could not be found.', 'error');
        inventoryForm.hidden = true;
        break;
      case 409:
        setStatus(
          'Inventory changed since you loaded this page. Reload the latest quantities before saving again.',
          'conflict'
        );
        conflictReloadButton.hidden = false;
        break;
      case 503:
        setStatus('Inventory service is temporarily unavailable. Please try again later.', 'error');
        break;
      default:
        setStatus('Inventory could not be updated right now. Please try again.', 'error');
        break;
    }
  };

  const getInventoryEndpoint = () =>
    `${config.apiBaseUrl}/api/admin/inventory/${encodeURIComponent(config.productId)}`;

  const confirmDiscardChanges = () =>
    !state.dirty || window.confirm('Discard your unsaved inventory changes?');

  const loadInventory = async ({ confirmDiscard = true } = {}) => {
    if (confirmDiscard && !confirmDiscardChanges()) {
      return;
    }

    inventoryForm.hidden = true;
    conflictReloadButton.hidden = true;
    reloadButton.disabled = true;
    productIdElement.textContent = `PRODUCT / ${config.productId}`;
    productNameElement.textContent = 'Loading inventory';
    setStatus('Retrieving current quantities\u2026');

    try {
      const response = await authorizedFetch(getInventoryEndpoint(), { method: 'GET' });
      if (!response) {
        return;
      }

      if (!response.ok) {
        console.error(`TACC admin inventory GET failed with status ${response.status}.`);
        handleApiError(response, 'inventory request');
        return;
      }

      const inventory = parseInventoryResponse(await response.json());
      renderInventory(inventory);
      setStatus('Current inventory loaded.', 'success');
    } catch (error) {
      console.error('TACC admin inventory could not be loaded.', error);
      setStatus('Inventory service is temporarily unavailable. Please try again later.', 'error');
    } finally {
      reloadButton.disabled = false;
    }
  };

  const saveInventory = async () => {
    if (state.saving || !state.inventory) {
      return;
    }

    const quantities = collectQuantities();
    if (!quantities || !inventoryForm.checkValidity()) {
      setStatus('One or more inventory quantities are invalid.', 'error');
      inventoryForm.reportValidity();
      return;
    }

    setBusy(true);
    setStatus('Saving inventory\u2026');

    try {
      const response = await authorizedFetch(getInventoryEndpoint(), {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          etag: state.inventory.etag,
          variants: quantities
        })
      });

      if (!response) {
        return;
      }

      if (!response.ok) {
        console.error(`TACC admin inventory PUT failed with status ${response.status}.`);
        handleApiError(response, 'inventory update');
        return;
      }

      const inventory = parseInventoryResponse(await response.json());
      renderInventory(inventory);
      setStatus('Inventory updated.', 'success');
    } catch (error) {
      console.error('TACC admin inventory could not be saved.', error);
      setStatus('Inventory service is temporarily unavailable. Please try again later.', 'error');
    } finally {
      setBusy(false);
    }
  };

  const signIn = async () => {
    authError.hidden = true;
    signInButton.disabled = true;
    signInButton.textContent = 'Redirecting\u2026';

    try {
      await state.msalInstance.loginRedirect({
        scopes: [config.apiScope],
        redirectUri: config.redirectUri,
        redirectStartPage: config.redirectUri
      });
    } catch (error) {
      console.error('TACC admin sign-in could not be started.', error);
      authError.hidden = false;
      signInButton.disabled = false;
      signInButton.textContent = 'Sign in';
    }
  };

  const signOut = async () => {
    if (!confirmDiscardChanges()) {
      return;
    }

    try {
      await state.msalInstance.logoutRedirect({
        account: state.account,
        postLogoutRedirectUri: config.redirectUri
      });
    } catch (error) {
      console.error('TACC admin sign-out could not be completed.', error);
      if (views.get('authorized')?.hidden === false) {
        setStatus('Sign-out could not be completed. Please try again.', 'error');
      }
    }
  };

  const initialize = async () => {
    if (!config) {
      setView('configuration');
      return;
    }

    state.msalInstance = new PublicClientApplication({
      auth: {
        clientId: config.clientId,
        authority: config.authority,
        redirectUri: config.redirectUri,
        postLogoutRedirectUri: config.redirectUri,
        navigateToLoginRequestUrl: true
      },
      cache: {
        cacheLocation: BrowserCacheLocation.SessionStorage
      },
      system: {
        loggerOptions: {
          piiLoggingEnabled: false,
          logLevel: LogLevel.Warning,
          loggerCallback: (level, message, containsPii) => {
            if (!containsPii && level <= LogLevel.Warning) {
              console.warn(`MSAL: ${message}`);
            }
          }
        }
      }
    });

    try {
      await state.msalInstance.initialize();
      const redirectResponse = await state.msalInstance.handleRedirectPromise();
      state.account = redirectResponse?.account ||
        state.msalInstance.getActiveAccount() ||
        state.msalInstance.getAllAccounts()[0] ||
        null;

      if (!state.account) {
        showSignedOut(false);
        return;
      }

      state.msalInstance.setActiveAccount(state.account);
      showAuthorized();
      await loadInventory({ confirmDiscard: false });
    } catch (error) {
      console.error('TACC admin authentication initialization failed.', error);
      showSignedOut(true);
    }
  };

  signInButton.addEventListener('click', signIn);
  signOutButtons.forEach((button) => button.addEventListener('click', signOut));
  reloadButton.addEventListener('click', () => loadInventory());
  conflictReloadButton.addEventListener('click', () => loadInventory());
  inventoryForm.addEventListener('submit', (event) => {
    event.preventDefault();
    saveInventory();
  });
  window.addEventListener('beforeunload', (event) => {
    if (!state.dirty) {
      return;
    }

    event.preventDefault();
    event.returnValue = '';
  });

  initialize();
})();
