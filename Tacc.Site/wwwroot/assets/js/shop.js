(() => {
  const PRODUCT_ID = 'tacc-shirt';
  const REQUEST_TIMEOUT_MS = 8000;
  const CHECKOUT_RESULT = new URLSearchParams(window.location.search).get('checkout');

  const setupProductMedia = () => {
    document.querySelectorAll('[data-product-media]').forEach((gallery) => {
      const tabs = [...gallery.querySelectorAll('[data-media-tab]')];
      const panels = [...gallery.querySelectorAll('[data-media-panel]')];

      const selectMedia = (selectedTab) => {
        const selectedPanelId = selectedTab.getAttribute('aria-controls');

        tabs.forEach((tab) => {
          const isSelected = tab === selectedTab;
          tab.setAttribute('aria-selected', String(isSelected));
          tab.tabIndex = isSelected ? 0 : -1;
        });

        panels.forEach((panel) => {
          const isSelected = panel.id === selectedPanelId;
          panel.hidden = !isSelected;

          panel.querySelectorAll('video').forEach((video) => {
            if (isSelected) {
              video.play().catch(() => {});
            } else {
              video.pause();
            }
          });
        });
      };

      tabs.forEach((tab, index) => {
        tab.addEventListener('click', () => selectMedia(tab));
        tab.addEventListener('keydown', (event) => {
          if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
            return;
          }

          event.preventDefault();
          const direction = event.key === 'ArrowRight' ? 1 : -1;
          const nextTab = tabs[(index + direction + tabs.length) % tabs.length];
          selectMedia(nextTab);
          nextTab.focus();
        });
      });
    });
  };

  setupProductMedia();

  const productElement = document.querySelector(`[data-inventory-product="${PRODUCT_ID}"]`);

  if (!productElement) {
    return;
  }

  const sizeSelector = productElement.querySelector('[data-size-selector]');
  const sizeOptions = productElement.querySelector('[data-size-options]');
  const sizeButtons = new Map();
  const stockMessages = new Map();
  const inventorySummary = productElement.querySelector('[data-inventory-summary]');
  const checkoutButton = productElement.querySelector('[data-checkout-button]');
  const checkoutStatus = productElement.querySelector('[data-checkout-status]');

  const state = {
    inventoryStatus: 'loading',
    variants: new Map(),
    selectedVariantId: null,
    checkoutPending: false
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

  const getCheckoutEndpoint = () =>
    `${getApiBaseUrl()}/api/stripe/checkout`;

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

    if (variants.size === 0) {
      throw new TypeError('Inventory response does not contain any variants.');
    }

    return variants;
  };

  const renderVariantControls = () => {
    sizeButtons.clear();
    stockMessages.clear();
    sizeOptions.replaceChildren();

    state.variants.forEach((variant, variantId) => {
      const option = document.createElement('div');
      option.className = 'size-option';

      const stockMessageId = `stock-${variantId.replace(/[^a-zA-Z0-9_-]/g, '-')}`;
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'size-button';
      button.dataset.variantId = variantId;
      button.setAttribute('aria-pressed', 'false');
      button.setAttribute('aria-describedby', stockMessageId);
      button.textContent = variantId;
      button.addEventListener('click', () => setSelectedVariant(variantId));

      const message = document.createElement('span');
      message.className = 'stock-message';
      message.id = stockMessageId;
      message.dataset.stockMessage = '';
      message.textContent = getStockMessage(variant.quantity);
      message.classList.toggle('is-unavailable', variant.quantity === 0);

      option.append(button, message);
      sizeOptions.append(option);
      sizeButtons.set(variantId, button);
      stockMessages.set(variantId, message);
    });
  };

  const setSelectedVariant = (variantId) => {
    state.selectedVariantId = variantId;

    sizeButtons.forEach((button, currentVariantId) => {
      button.setAttribute('aria-pressed', String(currentVariantId === variantId));
    });

    const selectedVariant = state.variants.get(variantId);
    const inventoryIsConfirmed = state.inventoryStatus === 'ready' && selectedVariant;
    const canProceed = inventoryIsConfirmed && selectedVariant.quantity > 0 && !state.checkoutPending;

    checkoutButton.disabled = !canProceed;

    if (state.inventoryStatus === 'error') {
      checkoutStatus.textContent = `${variantId} selected. Availability cannot be confirmed.`;
    } else if (!selectedVariant) {
      checkoutStatus.textContent = 'Choose a size to continue.';
    } else if (selectedVariant.quantity === 0) {
      checkoutStatus.textContent = `${variantId} selected. More coming soon.`;
    } else {
      checkoutStatus.textContent = `${variantId} selected. Ready for secure checkout.`;
    }
  };

  const renderReadyState = () => {
    state.inventoryStatus = 'ready';
    state.selectedVariantId = null;
    inventorySummary.textContent = 'Availability confirmed';
    renderVariantControls();
    sizeSelector.disabled = false;

    if (CHECKOUT_RESULT === 'success') {
      checkoutStatus.textContent = 'Payment received. Thank you—your order is confirmed.';
    } else if (CHECKOUT_RESULT === 'cancelled') {
      checkoutStatus.textContent = 'Checkout cancelled. Choose a size when you are ready.';
    }

    state.variants.forEach((variant, variantId) => {
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
    state.selectedVariantId = null;
    inventorySummary.textContent = 'Availability will be updated soon';
    sizeSelector.disabled = true;
    sizeButtons.clear();
    stockMessages.clear();
    sizeOptions.replaceChildren();
    checkoutButton.disabled = true;
    checkoutStatus.textContent = 'Checkout is unavailable while availability is being updated.';
  };

  const setCheckoutPending = (isPending) => {
    state.checkoutPending = isPending;
    checkoutButton.textContent = isPending ? 'Opening checkout…' : 'Checkout with Stripe';
    checkoutButton.setAttribute('aria-busy', String(isPending));
    sizeButtons.forEach((button) => {
      button.disabled = isPending;
    });

    const selectedVariant = state.variants.get(state.selectedVariantId);
    checkoutButton.disabled = isPending || !selectedVariant || selectedVariant.quantity <= 0;
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

  checkoutButton.addEventListener('click', async () => {
    if (checkoutButton.disabled || state.checkoutPending || !state.selectedVariantId) {
      return;
    }

    setCheckoutPending(true);
    checkoutStatus.textContent = 'Opening secure checkout…';

    try {
      const response = await fetch(getCheckoutEndpoint(), {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json'
        },
        cache: 'no-store',
        body: JSON.stringify({
          productId: PRODUCT_ID,
          variantId: state.selectedVariantId
        })
      });
      const payload = await response.json().catch(() => null);

      if (!response.ok) {
        if (response.status === 409) {
          await loadInventory();
        }

        throw new Error(payload?.error || 'Checkout could not be started.');
      }

      if (!payload || typeof payload.url !== 'string') {
        throw new TypeError('Checkout response did not contain a URL.');
      }

      const checkoutUrl = new URL(payload.url);
      if (checkoutUrl.protocol !== 'https:' || !checkoutUrl.hostname.endsWith('.stripe.com')) {
        throw new TypeError('Checkout response contained an unexpected URL.');
      }

      window.location.assign(checkoutUrl.href);
    } catch (error) {
      console.error('Unable to start Stripe Checkout.', error);
      checkoutStatus.textContent = error instanceof Error
        ? error.message
        : 'Checkout could not be started. Please try again.';
      setCheckoutPending(false);
    }
  });

  loadInventory();
})();
