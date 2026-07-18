# The Draft Room — Master Design System

> **Retrieval rule:** Before building a page, read this file and then check
> `pages/<page-name>.md`. A page file may override this master only for that page.

**Status:** Canonical visual direction and persisted product memory  
**Updated:** 14 July 2026  
**Stack:** React 18, TypeScript, Vite, Tailwind CSS 4, Lucide React  
**Design intelligence:** `ui-ux-pro-max`  
**Moodboard:** [`../../docs/assets/fc-draft-asset-moodboard.png`](../../docs/assets/fc-draft-asset-moodboard.png)  
**Design dials:** Variance 6/10 · Motion 4/10 · Density 6/10

The supplied asset moodboard is the visual source of truth. **ROSTR** is the canonical product name, matching the gold wordmark, "R" monogram, and "Draft. Strategize. Dominate." tagline in the moodboard. Skill-generated recommendations support accessibility, interaction, responsive behaviour, and implementation quality, but must not replace the moodboard's approved football-broadcast identity.

## 1. Brand foundation

**Positioning:** A private, premium, live football drafting experience where collaboration feels like serious competition.

**Brand promise:** Draft together. Win together.

**Attributes:** Vibrant, competitive, live, premium, broadcast, fast, collaborative, immersive.

**Visual character:** A premium editorial sports operations product: a crisp white canvas, black "tuxedo" navigation and hero panels, and rich gold as the jewelry — CTAs, active turn, trophy moments. Tactical geometry, confident condensed typography, brushed-metal and stadium-light textures. Dark mode translates the same hierarchy into a floodlit night-match atmosphere where the gold glows against matte black.

## 2. Source-asset status

- The moodboard contains the approved direction for the primary logo, horizontal wordmark, app-icon/monogram family, colour palette, typography, atmosphere, and interaction style.
- The board itself is a reference asset, not a production sprite sheet. Do not crop UI logos or icons directly from it for shipping.
- Obtain or create approved transparent SVG/PNG exports before replacing the current app mark. Preserve clear space and legibility at small sizes.
- Club crests, league marks, player photography, game branding, and typefaces require appropriate rights or licensed alternatives before production use.

## 3. Colour system

Brand palette: **Matte Black + Rich Gold on Crisp White** (ROSTR moodboard). Light mode is the primary and default application experience. Dark mode is fully supported as a persistent user selection. Both modes preserve the same gold brand hue and semantic hierarchy.

| Token | Hex | CSS variable | Use |
|---|---:|---|---|
| Crisp White (canvas) | `#FFFFFF` | `--color-bg` | Default light application background |
| Surface | `#FFFFFF` | `--color-surface` | Default cards, sheets, inputs |
| Raised Surface | `#F6F3EA` | `--color-surface-raised` | Selected surfaces and subtle grouping (warm gold tint) |
| Rich Gold | `#D4AF37` | `--color-primary` | Primary CTA, active turn, focus accent (fill only) |
| Antique Gold | `#B8860B` | `--color-secondary` | Live states, gradients, highlight moments |
| Matte Black | `#0A0A0A` | `--color-accent` | Tuxedo nav/hero panels, team accents, the second brand pillar |
| Gold Text (AA) | `#8A6A10` | `--color-primary-text` | Small gold text on white (5.1:1) — raw gold is 2.1:1 |
| Ink | `#141414` | `--color-text` | Primary text and icons in light mode |
| Warm Grey | `#6B6A63` | `--color-text-muted` | Secondary copy in light mode |
| Success | `#35D07F` | `--color-success` | Ready, connected, accepted |
| Warning | `#F5B942` | `--color-warning` | Timer warning and attention |
| Destructive | `#FF4D5E` | `--color-danger` | Errors, destructive actions, expiry |

**Tuxedo ink panels** — the sidebar, topbar, bottom nav, and hero are Matte Black in both themes so the black-and-gold signature frames every screen. Their tokens: `--color-ink-panel` `#0A0A0A`, `--color-on-ink` `#F5F3EC`, `--color-on-ink-muted` `#A8A69B`, `--color-ink-gold` `#D4AF37`, `--border-on-ink` `rgba(230,215,166,.16)`.

### Colour rules

- **Gold is a fill/accent hue, not a text hue on white.** Rich Gold `#D4AF37` reads at only 2.1:1 on white — never use it for small body text there. For gold text on a light surface use the AA bronze `--color-primary-text` (`#8A6A10`). On black panels, gold reads at 9:1+ and is the preferred accent.
- CTAs are solid Rich Gold with Matte Black text (`--color-on-primary` `#0A0A0A`, 9.4:1).
- The core brand gradient is a gold sheen `#B8860B → #D4AF37 → #F2E8C6`; reserve it for logos, hero moments, active progress, and celebratory states.
- Most content surfaces remain crisp white in the default theme so live state and player data stay legible; black is concentrated in the framing chrome (nav/hero).
- Dark mode maps the canvas, surface, raised surface, text, and muted tokens to `#0A0A0A`, `#141416`, `#1E1E21`, `#F5F3EC`, and `#A8A69B` respectively.
- Colour never carries team identity, pick status, or errors alone; pair it with labels, icons, numbers, or patterns.
- Body text and essential controls must meet WCAG 2.2 AA contrast. Never use low-opacity gold or grey for required information without checking the final surface pair.

## 4. Typography

- **Display direction:** `Colfax Condensed` from the moodboard, or an approved/licensed condensed athletic display face. Colfax is a licensed typeface — until licensing is confirmed, use **Barlow Condensed** as the self-hosted implementation fallback (the app ships Barlow Condensed today).
- **Body/UI:** **Inter**, weights 400, 500, 600, and 700.
- **Numbers/timers:** Inter 600/700 with tabular numerals; use the display face only when rapid scanning remains clear.
- Headings are bold, condensed, athletic, and may use uppercase sparingly. Body copy stays sentence case.
- Minimum body size is 16px with a 1.5 line-height. Metadata may use 12–14px only when it remains readable and non-essential information is not compressed excessively.

```css
@import url('https://fonts.googleapis.com/css2?family=Barlow+Condensed:ital,wght@0,600;0,700;0,800;1,700&family=Inter:wght@400;500;600;700&display=swap');
```

## 5. Shape, materials, and imagery

- Use 10–16px radii for application cards and controls; player cards may use sharper chamfered or clipped corners.
- Borders are thin graphite lines. Active elements may receive a one-pixel gold edge plus a restrained outer glow.
- Use glossy dark cards, subtle grain, carbon/premium materials, tactical lines, pitch geometry, stadium lights, locker-room corridors, and crowd silhouettes.
- Effects must frame the interface rather than compete with names, ratings, eligibility, the active team, or the clock.
- Avoid generic purple SaaS gradients, excessive glass blur, cartoon football motifs, emoji icons, and decorative imagery inside dense workflows.

## 6. Layout and responsive behaviour

| Token | Value | Typical use |
|---|---:|---|
| `--space-1` | `4px` | Micro gaps |
| `--space-2` | `8px` | Icon/label gaps |
| `--space-3` | `12px` | Compact control padding |
| `--space-4` | `16px` | Default component padding |
| `--space-6` | `24px` | Card and section gaps |
| `--space-8` | `32px` | Large section separation |
| `--space-12` | `48px` | Desktop composition spacing |

- Design mobile-first at 375px, then verify 768px, 1024px, and 1440px.
- Primary touch targets are at least 44×44px with at least 8px separation.
- Keep the active turn, active position, and timer visible in the live-draft viewport.
- On mobile, place the primary action within thumb reach and respect safe-area insets.
- Dense player lists may compress spacing, but must not reduce hit areas or hide essential stats.
- No core action may require hover or precise pointer input.

## 7. Component language

### Primary actions

- Solid Rich Gold background with Matte Black text for labels (9.4:1); white text on gold does not provide sufficient contrast.
- Minimum height 48px; 10–12px radius; Inter 600.
- Hover/pressed feedback uses brightness and a 1–2px visual lift/press without causing layout shift.
- Focus uses a clearly visible ink and/or gold ring with adequate offset.

### Secondary actions

- Surface fill or transparent background with graphite border and ink text.
- Gold border/text is reserved for high-salience alternatives, not every secondary control.

### Cards and lists

- Default cards use crisp white over the white canvas with a subtle warm-grey border and low elevation. Dark mode uses Charcoal over Matte Black.
- Selected or active cards use a gold edge and restrained glow; unavailable cards reduce emphasis while preserving readable labels.
- Player cards prioritize name, overall, eligible position/role, availability, and selection action before decorative metadata.

### Inputs

- Use visible labels; placeholders are examples, never the only label.
- Input height is at least 48px and uses semantic surface, border, and text tokens for both themes.
- Validation appears beside the field with text plus an icon; never communicate errors through colour alone.

### Icons

- Use Lucide outline SVG icons with a consistent stroke weight.
- Provide accessible names for icon-only controls and use filled/colour variants only to communicate an additional active state.
- Core families from the moodboard: draft, teams, players, timer, live, chat, notifications, lock, spinner, victory.

### Data tables

- Administrative directories use semantic tables with visible horizontal and vertical separators, persistent column headings, and readable row hover states.
- User lists provide search, explicit 10/25/50 page-size controls, current range/total, page count, and labelled previous/next actions.
- Tables may scroll horizontally at narrow widths, but pagination and primary actions remain outside the scroll region.

### Real-time activity

- The admin notification bell shows an unread count and opens a labelled activity panel.
- Player sign-ins and newly created draft rooms appear without refresh through an authenticated server event stream.
- Connection state is explicit (`Live` or `Reconnecting`), and recent events remain available after the panel is reopened during the current server session.

## 8. Motion

- Standard state transitions: 150–300ms. List/card entrance sequences may run 300–450ms with short staggering.
- Use transform and opacity for performance; avoid animating layout dimensions in live workflows.
- Lobby readiness: quick pulse and snap-to-slot.
- Pick accepted: short card lift and floodlight/tunnel reveal, then spatial movement into the squad.
- Turn change: pink-to-violet accent sweep across the timer and active roster slot.
- Timer warning: controlled pulse beginning at 15 seconds; danger colour at expiry. No screen shake.
- Reconnection: calm progress indicator followed by a brief synchronized confirmation.
- Respect `prefers-reduced-motion`; all critical state changes remain explicit without animation.

## 9. Accessibility and interaction priorities

1. WCAG 2.2 AA contrast, semantic structure, keyboard support, alternative text, and visible focus.
2. 44×44px touch targets, one-handed placement, loading feedback, and no hover-only behaviour.
3. Image optimization, reserved media dimensions, lazy loading, and minimal blur/glow cost.
4. Consistent light-first editorial sports style, equivalent dark mode, and SVG iconography.
5. Mobile-first responsive layout with no horizontal page scrolling.
6. Readable typography and semantic colour tokens.
7. Meaningful, reduced-motion-safe animation.
8. Visible labels, inline validation, and clear success/error feedback.
9. Predictable navigation, back behaviour, and deep links.

## 10. Anti-patterns

- Generic SaaS shells without sports character or action-red palettes.
- Electric lime as a primary brand colour; the moodboard supersedes the earlier lime direction.
- Neon glow on all text or surfaces.
- Low-contrast grey-on-black metadata.
- Multiple competing gradients in the same viewport.
- Emoji or mixed icon families.
- Dead controls, placeholder-only form labels, hidden focus rings, or status conveyed by colour alone.
- Decorative motion during time-critical selection, layout-shifting hover states, or animation without a reduced-motion path.
- Cropping the moodboard to manufacture production logos or icons.

## 11. Pre-delivery checklist

- [ ] Moodboard and this master file were reviewed before implementation.
- [ ] Page-specific override was checked and applied where present.
- [ ] Contrast meets WCAG 2.2 AA for text, controls, focus, and state indicators.
- [ ] All actions work with keyboard and touch; focus states are visible.
- [ ] Touch targets are at least 44×44px and have sufficient separation.
- [ ] Lucide/SVG icons are consistent and icon-only controls have accessible names.
- [ ] Loading, empty, success, error, unavailable, offline, and reconnecting states are covered.
- [ ] Motion communicates state, uses transform/opacity, and respects `prefers-reduced-motion`.
- [ ] Layout is verified at 375px, 768px, 1024px, and 1440px with no horizontal page scroll.
- [ ] Fixed navigation and sticky actions respect safe areas and do not hide content.
- [ ] Production imagery, logo exports, fonts, and third-party football assets have appropriate rights.
