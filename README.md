# TACC Website and API

TACC is a small static website for **TACC Surface Lock**, a precision skin-prep formula for indoor climbers, with a .NET Azure Functions backend for future merchandise inventory.

## Public site

The Phase 3 site is organized into three routes:

```text
/
|-- index.html              Product / Surface Lock overview
|-- how-to-use/
|   `-- index.html          Application instructions, session reset, FAQ, formula
`-- shop/
    `-- index.html          Future merchandise destination
```

All pages share:

- The matte-black, off-white, and icy-blue TACC visual system
- Desktop navigation and an accessible mobile menu
- Product, How To Use, Shop, and Instagram destinations
- Active-page navigation state and a shared footer
- `assets/css/styles.css` for styling
- `assets/js/site.js` for navigation behavior and shared link configuration

The homepage remains focused on Surface Lock positioning, performance benefits, approved climber feedback, and the existing Surface Lock purchase path. Detailed application guidance now lives only on `/how-to-use/`. The `/shop/` route is intentionally a restrained placeholder: it has no shirt pricing, inventory, size selection, or checkout integration.

## Instagram configuration

The actual TACC Instagram profile was not present in the repository when Phase 3 was implemented. Until it is supplied, Instagram links use the Instagram homepage as a neutral external fallback.

Replace this value near the top of `assets/js/site.js`:

```javascript
const INSTAGRAM_URL = 'INSTAGRAM_URL_HERE';
```

with the complete approved profile URL, for example `https://www.instagram.com/<approved-handle>/`. Do not remove the surrounding quotes.

## Product positioning

TACC Surface Lock is not chalk and not lotion. It is pre-session skin prep for indoor climbers seeking better tack, friction feel, and grip response on plastic and fiberglass holds.

Tagline:

```text
Your Skin Is Part of Your Technique.
```

## Local preview

The site remains plain static HTML, CSS, and JavaScript. From the repository root:

```powershell
python -m http.server 8080 --bind 127.0.0.1
```

Then open:

```text
http://127.0.0.1:8080/
http://127.0.0.1:8080/how-to-use/
http://127.0.0.1:8080/shop/
```

The site uses page-relative asset links and explicit `index.html` navigation targets, so it does not depend on the host rewriting directory URLs. It works at the domain root, under a hosting subpath, and when the HTML files are opened directly. A local web server is still recommended because it most closely matches production behavior.

## Backend

The existing `.NET 9` isolated-worker Azure Functions project remains under `api/`. Phase 3 does not call the API from the frontend and does not change its routes:

```http
GET /api/health
GET /api/inventory/{productId}
```

See `api/README.md` for backend configuration, Azurite setup, and inventory testing.

## Project constraints

- Keep custom site styling in `assets/css/styles.css`.
- Keep shared site behavior in `assets/js/site.js`.
- Do not add frontend inventory fetching until Phase 4.
- Do not add shirt quantities, availability messaging, size selection, pricing, or checkout in Phase 3.
- Keep `local.settings.json` and production credentials out of source control.
