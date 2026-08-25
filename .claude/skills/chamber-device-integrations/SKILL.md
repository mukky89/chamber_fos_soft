---
name: chamber-device-integrations
description: "Implement and diagnose SIKA TP Premium and Vötsch communication, Remote Control gating, HTTP logs/CSV export, device web links, and profile-completion e-mail integration in chamber_fos_soft. Use for work involving these laboratory devices or their temperature data."
---

# Chamber Device Integrations

Read [`../../../docs/DEVICE_INTEGRATIONS.md`](../../../docs/DEVICE_INTEGRATIONS.md)
completely before changing SIKA/Vötsch communication, device logs, Remote
Control behavior, device web links, or completion e-mails.

Essential invariants:

- Serialize SIKA HTTP operations through `_ioGate`; the embedded server is
  unreliable under concurrent requests.
- Monitoring may remain available with Remote Control off. Gate mutations by
  `Com_ExternWriteFlag`; never silently treat an unreadable flag as permission.
- Use `getTaskLog` for the index and `getTaskLogs?taskid=<ID>` for time series.
  Do not make CSV export depend on `getTaskLogCertData`.
- Preserve every CSV point. Downsample only the UI graph.
- Open SIKA/Vötsch pages at `http://<host>/`; do not reuse communication ports.
- E-mail or log failures must not interrupt chamber control or alter a completed
  profile's result.
- For e-mail delivery mirror `sylex_fos_dashboard/utils/mailer.js`: prefer the
  Brevo API (`POST https://api.brevo.com/v3/smtp/email`, header `api-key`, Brevo
  sender/to/content payload) and retain authenticated Brevo SMTP on port 587 as
  fallback. Never commit API keys, SMTP logins, or SMTP keys.
- Start live diagnostics read-only. Do not issue START, STOP, setpoint, register
  write, delete, or export-trigger commands without an explicit request and an
  operator-safe device state.
- Keep Bridge health observable through `bridge-status.json` and the desktop
  Admin card. Do not equate the WPF process with the separate Bridge Agent, and
  report missing dashboard endpoints as a real error rather than an online state.

Established implementation seams:

- Extend URL/register helpers in `SikaRestApiProtocol` and device I/O in
  `SikaTpClient`; do not bypass the client's gate from a view model.
- Keep SIKA log DTOs, JSON parsing, time merging, and lossless CSV writing in
  `SikaTaskLogs.cs`. `t` is elapsed seconds and absolute time is log `Start + t`.
- Expose operator state and async commands from `ChamberViewModel`; the existing
  SIKA log UI belongs under `ChamberView.xaml` → **Záznam**.
- Maintain Remote gating in both layers: disabled WPF commands for clarity and a
  fresh `Com_ExternWriteFlag` check in Core immediately before every mutation.
- Completion mail changes flow through `ProfileCompletionEmail`, `EmailNotifier`,
  and `EmailSender`; retain the plain-text fallback and CSV attachment.

Keep parsers in Core and UI state/commands in the WPF view model. Add realistic
fixture-based tests, then run the full solution tests and build from the canonical
document.
