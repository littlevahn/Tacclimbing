# TACC Surface Lock Landing Page

Single-page landing page for **TACC Surface Lock**, a performance skin care product for rock climbing and fingertip care.

## Current Build

- Static HTML landing page in `index.html`
- Custom styling in `assets/css/styles.css`
- Bootstrap 5.3 loaded by CDN for responsive grid/layout utilities
- No inline CSS or JavaScript in the HTML
- No custom JavaScript currently required
- Responsive layout tested at desktop and phone widths

## Design Direction

The page is inspired by the provided TACC label/logo reference:

- Near-black product-focused background
- Large uppercase typography
- White-to-light-blue gradient on the main `TACC` text
- Light blue divider lines and accent color
- Clean horizontal structure with strong section breaks
- Minimal, polished, performance-product feel
- Slight sci-fi/technical tone without making the page feel busy

The opening brand area is designed as a full-screen intro panel. The `TACC / Surface Lock / Stick Response` lockup fills the first viewport, then the user scrolls into the product information sections below.

## Page Sections

1. **Full-screen brand intro**
   - Text-based TACC logo treatment
   - Blue horizontal rule
   - Surface Lock and Stick Response label language

2. **Hero section**
   - Product name
   - Climbing skin care positioning
   - Product info call-to-action
   - 1 oz / 30 ml product highlight

3. **Product description**
   - Placeholder copy for climbing use cases
   - Fingertip care and performance positioning

4. **Product details**
   - Three feature cards:
     - Stick Response
     - Fingertip Care
     - Clean Application

5. **Contact**
   - Placeholder email
   - Placeholder phone
   - Placeholder contact message

## SEO Setup

The page includes initial SEO metadata for:

- TACC Surface Lock
- Rock climbing skin care
- Fingertip care
- Grip performance
- Stick response
- Climbing hand care

Open Graph and Twitter summary metadata are also included for better link previews.

## Content And Copy Still Needed

Before launch, the placeholder copy should be replaced with final brand/product language:

- Final product tagline
- Final product description
- Clear explanation of what Surface Lock does
- Who the product is for
- When climbers should use it
- Application instructions
- Drying time or usage timing, if applicable
- Ingredient or formula notes, if appropriate
- Skin type guidance or cautions
- Final feature/benefit copy for the three product detail cards
- Final CTA text
- Real contact email
- Real contact phone number, if needed
- Final retail, wholesale, gym, or distribution message

## Technical Notes

- Keep custom CSS in `assets/css/styles.css`
- Do not add inline `<style>` or `<script>` blocks to `index.html`
- Add JavaScript only if the page needs interaction, and place it in a separate file such as `assets/js/main.js`
- Keep Bootstrap linked before the custom stylesheet so custom styles override Bootstrap defaults
- The page can be previewed locally with a simple static server, for example:

```bash
python -m http.server 8080 --bind 127.0.0.1
```

Then open:

```text
http://127.0.0.1:8080/index.html
```
