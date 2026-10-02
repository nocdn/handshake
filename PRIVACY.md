# Privacy policy

Hammerspoon (Windows Edition) does not collect telemetry or transfer information to other networked systems unless specifically requested by the user or by a script the user installs or writes.

The application stores its configuration, recordings, runtime diagnostics, and JavaScript console logs locally under `%APPDATA%\HsWin`. Runtime diagnostics never record toast text, window titles, typed or swallowed key names, or clipboard contents; they replace shell command text with a fingerprint and length, redact URL query values, and reduce launched file paths to the file name. JavaScript console logs contain whatever the user's own script prints. Log files are kept only locally, and files older than 14 days are deleted automatically (the newest 20 are always kept). Users can remove all local data by deleting the `%APPDATA%\HsWin` directory after uninstalling the application.

User-authored scripts can explicitly request network or system interactions, including HTTP requests, opening URLs, launching processes, accessing the clipboard, querying applications, recording audio, and controlling input. Hammerspoon (Windows Edition) performs those actions only when directed by the user's configuration or an explicit user action. The author of a third-party configuration script is responsible for describing any data that script collects or transmits.

The application does not include advertising, analytics, user accounts, or an automatic data-upload service.
