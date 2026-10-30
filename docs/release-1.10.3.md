# willGo 1.10.3

The login dialog accepts the fixed local access code requested by the owner.
Matching is case-sensitive and ignores surrounding whitespace. It occurs before
the normal minimum account length, IP lookup and server validation, so this code
can enter the workspace without a network connection.

All other input retains existing account validation and denied/unavailable server
responses cannot grant regular account access. Remembering the code only prefills
the login dialog; users still explicitly submit login on every application start.
Closing the login dialog continues to exit the application.

This is a fixed client-side access mechanism included in every copy of this build;
anyone who knows the code can enter. It does not provide cloud speech recognition
or model API credentials. Existing settings and credentials are preserved.
