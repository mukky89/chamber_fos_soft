---
name: wpf-ux-ui
description: Design or review XAML, styles, controls, dialogs and operator workflows in the VotschVc3 WPF application. Use for visual layout, design tokens, accessibility, responsive behavior, Slovak UI copy and safety-oriented operator interactions. Do not use for chart rendering internals, JSON persistence, navigation architecture or core device logic.
---

# VotschVc3 WPF UX/UI

Preserve the existing dark laboratory-operator design system. Operators may work at a distance, under time pressure, or with gloves, so live state and safety must remain more prominent than decoration.

## Before changing UI

1. Inspect `App.xaml` resource merge order, the affected view, and relevant entries in `Themes/Styles.xaml`, `Themes/Icons.xaml`, and `Themes/CrispButtonStyles.xaml`.
2. Reuse an existing semantic resource, icon, control, or layout pattern before adding another one.
3. Check the same workflow in Classic and Professional control modes when both expose it.
4. Preserve Slovak terminology, diacritics, and device-specific visibility rules.

## Required behavior

- Keep live values, connection state, alarms, active profile state, and FBG ownership visible where the operator acts.
- Use `DangerButton` and the established confirmation behavior for dangerous device mutations.
- Prefer disabled controls for role or temporary state restrictions. Hide controls only when a device lacks the capability or the current layout deliberately omits it.
- Keep transient action results in `AppNotificationService`; keep persistent state inline. Do not add status text that makes dashboard cards jump in size.
- Do not replace command bindings with view handlers unless the behavior is intrinsically visual, focus-related, window-related, or input-device-specific.
- Do not hardcode a color, icon, button template, or spacing value when a semantic application resource already exists.
- Preserve keyboard focus visibility, disabled-state contrast, text wrapping, and layouts usable at the application's minimum window size.
- Do not apply blur or glow effects to interactive content. A separate non-hit-testable decoration layer is acceptable when it leaves text and icons crisp.
- Use controls and hit targets suitable for laboratory operation; do not make essential controls compact merely to fit more content.

## Routing

- Read [references/design-system.md](references/design-system.md) when choosing resources, components, or theme placement.
- Read [references/xaml-review-checklist.md](references/xaml-review-checklist.md) when creating or substantially changing XAML.
- For any graph surface, also use `wpf-charting`.
- For navigation or ViewModel ownership, also use `wpf-mvvm-navigation`.
- For persisted UI preferences, also use `wpf-settings-persistence`.

## Verification

Inspect the changed view at minimum size and maximized size. Verify keyboard focus, disabled state, long Slovak text, empty/error states, role restrictions, and relevant device variants. Build the WPF project on Windows.
