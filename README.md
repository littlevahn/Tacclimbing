# TACC Surface Lock Landing Page

Single-page landing page for **TACC Surface Lock**, a precision skin-prep formula for indoor climbers.

## Current Build

- Static HTML landing page in `index.html`
- Custom styling in `assets/css/styles.css`
- Bootstrap 5.3 loaded by CDN for responsive grid/layout utilities
- No inline CSS or JavaScript in the HTML
- No custom JavaScript currently required

## Product Positioning

**TACC Surface Lock** is not chalk and not lotion. It is a pre-session skin-prep formula for indoor climbing, designed for climbers who want better tack, friction feel, and grip response on plastic and fiberglass holds.

Tagline:

```text
Your Skin Is Part of Your Technique.
```

## Design Direction

- Matte black product-focused background
- Premium off-white text
- Icy blue accent color
- Sharp, minimal, technical presentation
- Indoor climbing and performance-focused tone
- Precision tool feel, not skincare softness

## Page Sections

1. **Full-screen brand intro**
   - Large TACC lockup
   - Surface Lock product name
   - Final tagline

2. **Hero section**
   - Indoor climbing skin-prep positioning
   - Product info call-to-action
   - Not chalk / not lotion product framing

3. **Product description**
   - Finalized explanation of TACC as a precision skin-prep formula
   - Plastic and fiberglass hold positioning

4. **What TACC Is**
   - Pre-session skin performance product
   - Application and rinse context
   - Modern gym and comp-style surface focus

5. **What Surface Lock Means**
   - Explanation of the controlled, tacky, responsive skin feel

6. **Product details**
   - Stick Response
   - Fingertip Care
   - Built For Indoor Holds

7. **Who It's For / When To Use It**
   - Indoor climbers focused on performance and skin feel
   - Board holds, slopers, pinches, plastic, fiberglass, and comp-style movement

8. **How To Use**
   - Drop application
   - Rub-in timing
   - Water-only rinse
   - Dry, chalk, climb, and reapply guidance

9. **Formula Notes**
   - Glycerin
   - 1,3-Propanediol
   - Isopropyl Alcohol

10. **FAQ**
    - Chalk, lotion, grease, chalk compatibility, intended surfaces, and timing

11. **Contact**
    - Product availability
    - Gym inquiries
    - Retail and wholesale opportunities
    - Questions from climbers, gyms, and shops

## SEO Setup

The page metadata is aligned around:

- TACC Surface Lock
- Indoor climbing skin prep
- Better tack
- Friction feel
- Grip response
- Plastic and fiberglass climbing holds

## Technical Notes

- Keep custom CSS in `assets/css/styles.css`
- Keep Bootstrap linked before the custom stylesheet so custom styles override Bootstrap defaults
- The page can be opened directly as a static HTML file, or previewed with a simple static server:

```bash
python -m http.server 8080 --bind 127.0.0.1
```

Then open:

```text
http://127.0.0.1:8080/index.html
```
