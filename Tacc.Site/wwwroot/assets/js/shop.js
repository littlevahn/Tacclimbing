(() => {
  const PRODUCT_ID = 'tacc-shirt';
  const DISPLAY_VARIANT_IDS = Object.freeze(['S', 'M', 'L', 'XL']);
  const REQUEST_TIMEOUT_MS = 8000;

  const productElement = document.querySelector(`[data-inventory-product="${PRODUCT_ID}"]`);

  if (!productElement) {
    return;
  }

  const sizeSelector = productElement.querySelector('[data-size-selector]');
  const sizeButtons = new Map(
    [...productElement.querySelectorAll('[data-variant-id]')]
      .map((button) => [button.dataset.variantId, button])
  );
  const stockMessages = new Map(
    [...productElement.querySelectorAll('[data-variant-id]')]
      .map((button) => [button.dataset.variantId, document.getElementById(`stock-${button.dataset.variantId}`)])
  );
  const inventorySummary = productElement.querySelector('[data-inventory-summary]');
  const checkoutButton = productElement.querySelector('[data-checkout-button]');
  const checkoutStatus = productElement.querySelector('[data-checkout-status]');

  const state = {
    inventoryStatus: 'loading',
    variants: new Map(),
    selectedVariantId: null
  };

  const getStockMessage = (quantity) => {
    if (quantity >= 10) {
      return 'Available';
    }

    if (quantity > 5) {
      return 'Limited stock';
    }

    if (quantity > 0) {
      return `${quantity} remaining`;
    }

    return 'More coming soon';
  };

  const getApiBaseUrl = () => {
    const configuredBaseUrl = window.TACC_CONFIG?.apiBaseUrl;

    if (typeof configuredBaseUrl !== 'string') {
      return '';
    }

    return configuredBaseUrl.trim().replace(/\/+$/, '');
  };

  const getInventoryEndpoint = () =>
    `${getApiBaseUrl()}/api/inventory/${encodeURIComponent(PRODUCT_ID)}`;

  const parseInventoryResponse = (payload) => {
    if (!payload || typeof payload !== 'object' || payload.productId !== PRODUCT_ID || !Array.isArray(payload.variants)) {
      throw new TypeError('Inventory response has an unexpected product structure.');
    }

    const variants = new Map();

    payload.variants.forEach((variant) => {
      if (!variant || typeof variant !== 'object' || typeof variant.variantId !== 'string') {
        throw new TypeError('Inventory response contains a malformed variant.');
      }

      if (variants.has(variant.variantId)) {
        throw new TypeError('Inventory response contains duplicate variants.');
      }

      if (!Number.isSafeInteger(variant.quantity) || variant.quantity < 0) {
        throw new TypeError('Inventory response contains an invalid quantity.');
      }

      variants.set(variant.variantId, {
        variantId: variant.variantId,
        quantity: variant.quantity
      });
    });

    DISPLAY_VARIANT_IDS.forEach((variantId) => {
      if (!variants.has(variantId)) {
        throw new TypeError('Inventory response is missing a required shirt variant.');
      }
    });

    return variants;
  };

  const setSelectedVariant = (variantId) => {
    state.selectedVariantId = variantId;

    DISPLAY_VARIANT_IDS.forEach((currentVariantId) => {
      sizeButtons.get(currentVariantId)?.setAttribute('aria-pressed', String(currentVariantId === variantId));
    });

    const selectedVariant = state.variants.get(variantId);
    const inventoryIsConfirmed = state.inventoryStatus === 'ready' && selectedVariant;
    const canProceed = inventoryIsConfirmed && selectedVariant.quantity > 0;

    checkoutButton.disabled = !canProceed;

    if (state.inventoryStatus === 'error') {
      checkoutStatus.textContent = `${variantId} selected. Availability cannot be confirmed.`;
    } else if (!selectedVariant) {
      checkoutStatus.textContent = 'Choose a size to continue.';
    } else if (selectedVariant.quantity === 0) {
      checkoutStatus.textContent = `${variantId} selected. More coming soon.`;
    } else {
      checkoutStatus.textContent = `${variantId} selected. Checkout integration is coming in the next phase.`;
    }
  };

  const renderReadyState = () => {
    state.inventoryStatus = 'ready';
    inventorySummary.textContent = 'Availability confirmed';
    sizeSelector.disabled = false;

    DISPLAY_VARIANT_IDS.forEach((variantId) => {
      const variant = state.variants.get(variantId);
      const button = sizeButtons.get(variantId);
      const message = stockMessages.get(variantId);

      button.disabled = false;
      message.textContent = getStockMessage(variant.quantity);
      message.classList.remove('is-loading');
      message.classList.toggle('is-unavailable', variant.quantity === 0);
    });
  };

  const renderFailureState = () => {
    state.inventoryStatus = 'error';
    state.variants = new Map();
    inventorySummary.textContent = 'Availability will be updated soon';
    sizeSelector.disabled = false;
    checkoutButton.disabled = true;
    checkoutStatus.textContent = 'Checkout is unavailable while availability is being updated.';

    DISPLAY_VARIANT_IDS.forEach((variantId) => {
      const button = sizeButtons.get(variantId);
      const message = stockMessages.get(variantId);

      button.disabled = false;
      message.textContent = 'More coming soon';
      message.classList.remove('is-loading');
      message.classList.add('is-unavailable');
    });
  };

  const loadInventory = async () => {
    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

    try {
      const response = await fetch(getInventoryEndpoint(), {
        method: 'GET',
        headers: { Accept: 'application/json' },
        cache: 'no-store',
        signal: controller.signal
      });

      if (!response.ok) {
        throw new Error('Inventory request was not successful.');
      }

      state.variants = parseInventoryResponse(await response.json());
      renderReadyState();
    } catch (error) {
      renderFailureState();
      console.error('Unable to retrieve TACC inventory.', error);
    } finally {
      window.clearTimeout(timeoutId);
    }
  };

  sizeButtons.forEach((button, variantId) => {
    button.addEventListener('click', () => setSelectedVariant(variantId));
  });

  checkoutButton.addEventListener('click', () => {
    if (checkoutButton.disabled) {
      return;
    }

    checkoutStatus.textContent = `${state.selectedVariantId} selected. Checkout is not active yet.`;
  });

  loadInventory();
})();
