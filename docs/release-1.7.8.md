# willGo 1.7.8

Settings now contain four top-level sections:

- Model configuration: separate screenshot/conversation endpoints, primary and
  fallback selection, interface protocols and timeout/retries. Screenshot
  prompts and phone capture mode/region remain inside the screenshot page.
- Speech settings: Doubao key and legacy credentials, connection test/service
  links, microphone and speaker selection, recording/transcript directory,
  utterance pause, conversation prompt and microphone trigger phrases.
- Quick questions: phone preset title/content, enable, add, move and delete.
- Appearance: capture exclusion, window opacity and answer font size.

Visible labels, option names, region summaries and new/default question presets
are Chinese. Localized choice records retain the existing protocol/routing enum
values. Existing endpoints, credentials, technical model names, URLs, custom
prompts, question IDs and saved settings are preserved.

The Settings window uses the same 16px native WindowChrome frame and invisible
resize hit area as the main workspace. Pages use unframed section headings,
rounded tabs, fields, credential controls and dropdowns. Content scrolls inside
each page while save/cancel/default buttons remain fixed at the bottom. The
minimum 800x560 layout retains usable fields and scrolling. Validation selects
the speech page before focusing an invalid archive directory.

Regression coverage exercises section organization, Chinese labels, independent
model editing, localized enum bindings and actual dropdown item realization,
speech text/device/path bindings, masked credential retention, question editing
and ordering, appearance controls, minimum-size layouts, defaults and modal
save/cancel behavior. Changes remain isolated in an editable snapshot until the
dialog is accepted. Native screen tests inspect Settings corner pixels, frame
geometry and resize targets alongside the existing workspace checks.
