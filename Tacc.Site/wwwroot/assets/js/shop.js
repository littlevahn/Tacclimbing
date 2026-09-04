(() => {
  const PRODUCT_ID = 'tacc-shirt';
  const REQUEST_TIMEOUT_MS = 8000;
  const CHECKOUT_TIMEOUT_MS = 15000;

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
  const sizeOptions = productElement.querySelector('[data-variant-list]');
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
    `${getApiBaseUrl()}/inventory/${encodeURIComponent(PRODUCT_ID)}`;

  const getCheckoutEndpoint = () => `${getApiBaseUrl()}/stripe/checkout`;

  const parseInventoryResponse = (payload) => {
    if (!payload || typeof payload !== 'object' || payload.productId !== PRODUCT_ID ||
      !Array.isArray(payload.variants) || payload.variants.length === 0) {
      throw new TypeError('Inventory response has an unexpected product structure.');
    }

    const variants = new Map();

    payload.variants.forEach((variant) => {
      if (!variant || typeof variant !== 'object' ||
        typeof variant.variantId !== 'string' || variant.variantId.trim() === '') {
        throw new TypeError('Inventory response contains a malformed variant.');
      }

      const variantId = variant.variantId.trim();

      if (variants.has(variantId)) {
        throw new TypeError('Inventory response contains duplicate variants.');
      }

      if (!Number.isSafeInteger(variant.quantity) || variant.quantity < 0) {
        throw new TypeError('Inventory response contains an invalid quantity.');
      }

      variants.set(variantId, {
        variantId,
        quantity: variant.quantity
      });
    });

    return variants;
  };

  const createVariantOption = (variant, index) => {
    const option = document.createElement('div');
    option.className = 'size-option';

    const messageId = `stock-variant-${index}`;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'size-button';
    button.dataset.variantId = variant.variantId;
    button.setAttribute('aria-pressed', 'false');
    button.setAttribute('aria-describedby', messageId);
    button.disabled = true;
    button.textContent = variant.variantId;

    const message = document.createElement('span');
    message.id = messageId;
    message.className = 'stock-message is-loading';
    message.dataset.stockMessage = '';
    message.textContent = 'Checking availability…';

    button.addEventListener('click', () => setSelectedVariant(variant.variantId));
    option.append(button, message);
    sizeButtons.set(variant.variantId, button);
    stockMessages.set(variant.variantId, message);
    return option;
  };

  const renderVariantOptions = () => {
    sizeButtons.clear();
    stockMessages.clear();
    sizeOptions.replaceChildren(
      ...[...state.variants.values()].map((variant, index) => createVariantOption(variant, index))
    );
  };

  const setSelectedVariant = (variantId) => {
    if (state.checkoutPending) {
      return;
    }

    state.selectedVariantId = variantId;

    sizeButtons.forEach((button, currentVariantId) => {
      button.setAttribute('aria-pressed', String(currentVariantId === variantId));
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
      checkoutStatus.textContent = `${variantId} selected. Ready for secure checkout.`;
    }
  };

  const setCheckoutPending = (isPending) => {
    state.checkoutPending = isPending;
    sizeButtons.forEach((button) => {
      button.disabled = isPending;
    });

    const selectedVariant = state.variants.get(state.selectedVariantId);
    checkoutButton.disabled = isPending || state.inventoryStatus !== 'ready' ||
      !selectedVariant || selectedVariant.quantity <= 0;
    checkoutButton.textContent = isPending ? 'Opening checkout…' : 'Checkout';
  };

  const getCheckoutUrl = (payload) => {
    if (!payload || typeof payload !== 'object' || typeof payload.url !== 'string') {
      throw new TypeError('Checkout response did not include a URL.');
    }

    const checkoutUrl = new URL(payload.url);
    if (checkoutUrl.protocol !== 'https:' ||
      (checkoutUrl.hostname !== 'stripe.com' && !checkoutUrl.hostname.endsWith('.stripe.com'))) {
      throw new TypeError('Checkout response included an unsupported URL.');
    }

    return checkoutUrl.href;
  };

  const startCheckout = async () => {
    const selectedVariant = state.variants.get(state.selectedVariantId);
    if (state.checkoutPending || state.inventoryStatus !== 'ready' ||
      !selectedVariant || selectedVariant.quantity <= 0) {
      return;
    }

    const controller = new AbortController();
    const timeoutId = window.setTimeout(() => controller.abort(), CHECKOUT_TIMEOUT_MS);
    setCheckoutPending(true);
    checkoutStatus.textContent = `Opening secure checkout for size ${selectedVariant.variantId}…`;

    try {
      const response = await fetch(getCheckoutEndpoint(), {
        method: 'POST',
        headers: {
          Accept: 'application/json',
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          productId: PRODUCT_ID,
          variantId: selectedVariant.variantId
        }),
        cache: 'no-store',
        signal: controller.signal
      });

      if (!response.ok) {
        if (response.status === 409) {
          selectedVariant.quantity = 0;
          const stockMessage = stockMessages.get(selectedVariant.variantId);
          stockMessage.textContent = getStockMessage(0);
          stockMessage.classList.add('is-unavailable');
          throw new Error('The selected size just sold out. Please choose another size.');
        }

        throw new Error('Secure checkout could not be started. Please try again.');
      }

      const checkoutUrl = getCheckoutUrl(await response.json());
      window.location.assign(checkoutUrl);
    } catch (error) {
      const message = error?.name === 'AbortError'
        ? 'Checkout took too long to respond. Please try again.'
        : error?.message || 'Secure checkout could not be started. Please try again.';
      checkoutStatus.textContent = message;
      console.error('Unable to start TACC checkout.', error);
      setCheckoutPending(false);
    } finally {
      window.clearTimeout(timeoutId);
    }
  };

  const renderReadyState = () => {
    state.inventoryStatus = 'ready';
    renderVariantOptions();
    inventorySummary.textContent = 'Availability confirmed';
    sizeSelector.disabled = false;

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
    sizeButtons.clear();
    stockMessages.clear();
    sizeOptions.replaceChildren();
    inventorySummary.textContent = 'Availability will be updated soon';
    sizeSelector.disabled = true;
    checkoutButton.disabled = true;
    checkoutStatus.textContent = 'Checkout is unavailable while availability is being updated.';
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

  checkoutButton.addEventListener('click', startCheckout);

  loadInventory();
})();
