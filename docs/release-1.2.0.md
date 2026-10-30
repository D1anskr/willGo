# willGo 1.2.0

Battle mode accepts screenshot questions alongside spoken questions. Ctrl+Shift+S
opens the existing regional screenshot selector. The battle toolbar provides
region capture and phone QR buttons; the floating ball's capture command also
routes into battle mode. Each completed capture creates a separate opponent
question with a retained preview and answer. Retry resends the same image and
updates the same question. Cancelling region selection creates no empty entry.

Phone captures use the existing QR page and configured full-desktop or preset
region mode. While battle mode is active, the HTTP /capture endpoint appends
to the battle list, including when audio capture is stopped. Returning to normal
mode restores the original capture flow. Concurrent screenshot operations are
rejected; answer requests retain the existing concurrency and queue limits.

Model settings have Screenshot and Conversation tabs, each with independent
primary/fallback endpoints, preferred target, credentials, protocol, model,
timeout and retries. Existing Primary/Secondary fields remain the screenshot
route; old files migrate copies of those endpoints into the conversation route.
Conversation requests retain Battle.ConversationPrompt. Screenshot requests
use the screenshot endpoints' SystemPrompt and include PNG image input.
All four API keys are protected with the existing Windows DPAPI helper on save.

Ctrl+Shift+S registers only while battle mode is active. If another desktop tool
has already registered it, a low-level keyboard hook handles only this chord in
battle mode, suppresses its repeated keydown events, and releases on leaving
battle mode or process exit. Other shortcuts retain their existing behavior.
The original software's shortcut settings are not changed.

Region selection temporarily allows mouse input when the app was click-through,
and restores click-through after completion or cancellation. Capture hides app
windows and restores them even on cancellation. Cancelling outstanding work also
cancels active screenshot selection; shutdown waits for screenshot cleanup.

Validation: 57 battle/configuration/UI checks, 62 Windows interaction checks,
8 complete application startup/routing checks. Coverage includes real HTTP
phone capture, multimodal image bytes, per-question answer association, separate
model selection, DPAPI persistence, legacy migration, retry/cancel, white-mode
selection, real overlay cancellation, occupied-shortcut native keyboard input,
and release of screenshot listeners on mode exit.
The configured screenshot model also answered a generated image containing
"What is 7 + 5?" correctly. No real desktop image was uploaded for that test.
The model configuration UI and screenshot answer view were rendered and inspected.
