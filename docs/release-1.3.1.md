# willGo 1.3.1

Restore account ID validation before entering the battle workspace. The login
contract and endpoint come from the earlier local Windows login implementation:
POST userId and the current IPv4 address to the existing willGo validation API.
Explicit valid, first-bind and whitelist results remain supported.

Only a successful allowed response can complete login. Missing/disabled users,
IP mismatch, unexpected responses, malformed JSON, network errors, timeouts and
server errors do not grant access. The older offline bypass is intentionally
omitted: every launch requires validation, even when an account ID is remembered.

No workspace, tray actions, feature hotkeys or phone capture listener are created
before validation. Closing login cancels an outstanding request and exits. A
dialog success result without a corresponding allowed validation cannot create
the workspace. After login, the single-window UI and manual speech switch remain.

RememberUserId and RememberedUserId are persisted and cloned with existing
configuration. Remembering only prefills the ID and never persists authorization.
Existing screenshot/conversation credentials and configuration remain supported.

Validation includes protocol fixtures, denied/invalid/unavailable responses,
remembered-ID persistence, close-during-validation, full startup rejection and
successful transition to the workspace. Integration tests replace authentication
and speech services with test doubles; no real account is bound during tests.
