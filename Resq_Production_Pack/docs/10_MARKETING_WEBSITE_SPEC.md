# Marketing website — playresq.com

## 1. Purpose and truth standard

Sell the game's central feeling through real cause-and-effect gameplay: a small intervention saves a little world. The website is not a dashboard, AI service, or generic technology landing page. Its primary task is to help a visitor understand the game, see evidence, and reach the real available next step.

`playresq.com` is an intended domain, not verified as available or owned. No deployment, purchase, account connection, or DNS change has been performed by this pack. Keep the site independently deployable from the Unity game. The game must work offline and must not depend on website uptime.

## 2. Mobbin research workflow

In Codex, discover the actual authenticated Mobbin tools. The official Mobbin MCP service documents Codex compatibility; connection and plan access must be confirmed in the user's environment. This conversation did not inspect any Mobbin screens. [S13]

Research three narrow needs: an entertainment/game launch page with strong gameplay media; an editorial page with clear readable information hierarchy; and an accessible support or feature-explanation flow. Retrieve actual screenshots/details, not just search-result titles. Choose one dominant composition reference and at most two narrowly borrowed patterns.

Write `web/design/reference-lock.md` containing reference IDs/URLs, observed traits, what to adapt, what to reject, a content hierarchy, media plan, exact tokens, and an annotated layout. Do not copy a distinctive logo, illustration, text, layout wholesale, or game assets. License screenshots according to their use; they are research material, not automatically publishable site media.

The proposed direction below is a product-specific starting brief. Refine it from real research before implementation. An unavailable provider is a blocker for that requested research step, not a reason to claim it was completed. Continue website content modeling, asset preparation, and tests that do not require the reference lock.

## 3. Proposed visual direction

**Miniature rescue field guide.** Combine a large, confident game wordmark, close-up tactile game imagery, and compact labels that resemble a well-designed operations guide. Preserve spacious composition and an unmistakable gameplay focus. Do not turn it into a dense military interface.

| Token | Proposed role |
|---|---|
| Deep green `#132B2A` | Main ink, dark media sections, restrained interface depth |
| Warm paper `#F3EBDD` | Information backgrounds and readable long-form sections |
| Rescue orange `#FF6B35` | Primary actions and meaningful selection accents; not body text on paper |
| River blue `#4A9EBD` | Water-related illustrative annotation, never sole warning cue |
| Leaf green `#6D9154` | World imagery and positive-state illustration |
| Charcoal `#192320` | High-contrast text where needed |

Test all actual text/background pairs; a palette is not accessibility certification. Use one readable sans-serif family with a licensed bold display cut and normal/medium text weights. Codex should choose the final family from licensed available fonts after reference research, record its license, and self-host a small subset. Avoid a second decorative font just for one hero word.

Use an 8 px spacing base, a 1,200–1,360 px content limit, large editorial section spacing, restrained radii, clear borders where necessary, and large media surfaces. The key shape is an open rescue-route line that can subtly echo the Q; do not use a medical cross or copy a rescue service emblem.

No purple glow, revolving gradient blobs, generic AI mascots, endless pill badges, floating SaaS dashboards, invented awards, fake testimonials, fabricated store ratings, empty pricing tiers, or identical three-card sections repeated down the page.

## 4. Homepage composition and copy

**Navigation:** RESQ wordmark; Gameplay; Worlds; Accessibility; Press. Primary CTA is determined by the actual release state. Compact mobile navigation retains keyboard/focus behavior.

**Hero:** H1 “Small worlds. Big saves.” Supporting copy: “Redirect the danger. Build a way out. Bring everyone home—in a miniature rescue game made for seated play.” Primary CTA: “Watch gameplay” only when a real published or locally hosted approved clip exists. Secondary CTA: “Explore the game.” Supporting label: “Hands-first rescue strategy for Meta Quest.” Do not display a compatibility claim beyond tested devices in a release.

Show a real close-up sequence: reservoir rising → player places barrier → water reroutes → boat reaches residents. The landing view must communicate what a player actually does. On constrained or reduced-motion clients, show a real still with a clear Play button; do not autoplay large audio/video.

**Section 2: Three actions, one consequence.** “Read the risk.” “Make your move.” “Bring them home.” Each explanation pairs with its own short real clip or screenshot. It is a narrative sequence, not a feature-icon wall.

**Section 3: A rescue that changes when you do.** An annotated scene shows two viable approaches and one consequence. Use authored arrows as explanatory overlays, clearly distinct from actual game UI. Do not claim both strategies are validated until their replays exist.

**Section 4: Meet the worlds.** Released worlds use real game media. Unreleased worlds are segregated into a clearly labeled “Planned worlds” area or omitted for the competition microsite. Never combine concept art with playable-world descriptions without labels.

**Section 5: Made for your seat.** Explain seated setup, hand tracking, one-hand support, optional Command mode, readable warnings, pause, and table fallback. Only ship claims that passed the corresponding test. Do not say “zero motion sickness” or “accessible to everyone.”

**Section 6: A closer look.** A small real screenshot gallery and a grounded description of the first chapter. Captions identify the build, world, or mode where useful.

**Footer:** Relevant real availability CTA; Support; Accessibility; Privacy; Press; copyright with verified owner identity. No unrelated agency logos or invented sponsor association.

## 5. Routes and content model

| Route | Content and acceptance |
|---|---|
| `/` | Real gameplay hook and available next step |
| `/how-to-play` | Read/plan/place/dispatch/rescue; controls, failure recovery, beginner FAQ |
| `/worlds` | Released catalogue, separately labeled future plans if displayed |
| `/worlds/[slug]` | World-specific gameplay, actual missions/media, implementation status |
| `/accessibility` | Input matrix, seated reach, dwell settings, captions, limitations, reset/fallback |
| `/press` | Approved wordmark, actual screenshots, short description, real factsheet, credits/contact |
| `/support` | Setup, permission fallback, saving, troubleshooting, actual contact destination |
| `/privacy` | Truthful local-data and website-services statement matching implementation |
| `/competition` | Optional frozen judge information with build/access instructions; no secrets or private invitations |
| `/404` | Small branded error page with a functional route home |

No user accounts, store checkout, subscriptions, or CMS are required for the competition website. Write content in typed local data/MDX, validate links and media, and keep status fields explicit: `planned`, `implemented`, `qualified`, `released`. Public compatibility uses `qualified` evidence, not roadmap status.

A waitlist is optional, not a fake form. If no authenticated email provider/backend is configured, omit collection. If later authorized, implement server-side validation, rate limiting, consent, confirmation, unsubscribe/deletion handling, accessible errors, and a privacy statement. Never show success when a message was not stored/sent. Do not add a database solely to collect speculative leads.

## 6. Implementation

Use a current compatible, pinned Next.js/TypeScript release with static rendering for content pages and small interactive islands. Discover and lock versions in the actual repo; this pack does not claim a future package version. Avoid heavy in-browser 3D on the primary path. A lightweight optional model viewer belongs below the fold and must have an equivalent still.

Serve responsive image sizes and video posters. Load video only on user request or under a carefully tested lightweight muted policy; respect reduced motion/data preferences where available. Use semantic HTML, proper heading hierarchy, labels, focus styles, keyboard-operable galleries, and descriptive alt text. Add titles, descriptions, canonicals only after the real host is configured, sitemap, robots, Open Graph media, and appropriate truthful structured data. Never generate ratings or award schema without evidence.

Initial project budgets: mobile Lighthouse performance and accessibility targets ≥90, LCP ≤2.5 s, CLS ≤0.1, and field INP ≤200 ms when enough real traffic exists to measure it. These are acceptance targets, not measured performance. A local synthetic test cannot report a field metric as collected.

## 7. Build and deployment gates

Run type checking, linting, content validation, production build, browser tests, media checks, accessibility automation, and visual review at 320, 360, 768, and 1440 px widths. Test keyboard order, reduced motion, long headings, slow media, modal focus recovery, unavailable video, and missing optional contact configuration.

Discover the actual Vercel team/project and verify repository/environment scope. Deploy a preview first, test the deployed site, then stage/promote production only within explicit authorized scope. Record commit, deployment ID, URL, test report, and environment. Do not guess resource IDs or print secrets. A successful build is not a production deployment. [S16]

After the user purchases the domain, verify ownership and inspect Vercel's exact DNS instructions for that project. Apply only authorized changes; do not overwrite unrelated records or hardcode a remembered IP. Until then, use the returned real preview host and describe the custom domain as pending. [S17]

## 8. Website completion definition

The site is complete only when actual media, tested links, truthful public copy, accessible interactions, deployment verification, and the correct release state are present. A polished shell full of placeholders is an internal prototype, not a finished marketing site. Capture desktop/mobile screenshots and compare them to the reference lock; correct design drift before publishing.
