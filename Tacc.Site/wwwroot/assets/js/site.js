(() => {
  const INSTAGRAM_URL = 'INSTAGRAM_URL_HERE';
  const instagramLinks = document.querySelectorAll('[data-instagram-link]');

  if (INSTAGRAM_URL !== 'INSTAGRAM_URL_HERE') {
    instagramLinks.forEach((link) => {
      link.href = INSTAGRAM_URL;
    });
  } else {
    instagramLinks.forEach((link) => {
      link.setAttribute('aria-label', 'Instagram (TACC profile link to be configured)');
    });
  }

  const menuButton = document.querySelector('[data-menu-toggle]');
  const navigation = document.querySelector('[data-site-navigation]');

  if (menuButton && navigation) {
    const setMenuOpen = (isOpen) => {
      menuButton.setAttribute('aria-expanded', String(isOpen));
      navigation.classList.toggle('is-open', isOpen);
      document.body.classList.toggle('menu-is-open', isOpen);
    };

    menuButton.addEventListener('click', () => {
      setMenuOpen(menuButton.getAttribute('aria-expanded') !== 'true');
    });

    navigation.addEventListener('click', (event) => {
      if (event.target.closest('a')) {
        setMenuOpen(false);
      }
    });

    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && menuButton.getAttribute('aria-expanded') === 'true') {
        setMenuOpen(false);
        menuButton.focus();
      }
    });

    window.matchMedia('(min-width: 761px)').addEventListener('change', (event) => {
      if (event.matches) {
        setMenuOpen(false);
      }
    });
  }

  document.getElementById('buy-tacc')?.addEventListener('click', () => {
    if (typeof window.gtag === 'function') {
      window.gtag('event', 'begin_checkout', {
        currency: 'USD',
        value: 20.00,
        items: [{
          item_id: 'tacc-surface-lock',
          item_name: 'TACC Surface Lock',
          price: 20.00,
          quantity: 1
        }]
      });
    }
  });
})();
