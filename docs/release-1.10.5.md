# willGo 1.10.5

The rounded login window now starts at explicit dimensions, with minimum sizes
and desktop work-area limits. This removes automatic height sizing from the
custom-chrome login window after a native window was observed at only two pixels
wide on the user's multiple-monitor setup.

Opening another instance now signals the running process to restore its existing
login window or authenticated workspace. It no longer opens an already-running
message instead of restoring the application. Activation never skips login.

Verification includes native window dimensions, monitor containment, normal and
hidden launch styles and a real second-process activation test. Login artwork,
copy, account validation, local access and saved settings are preserved.
