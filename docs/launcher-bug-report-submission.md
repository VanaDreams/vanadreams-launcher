# Launcher bug-ticket submission

The Setup page Send bug report action opens `https://vanadreams.org/report/?source=launcher&version=...`.
Only the public launcher version is placed in the URL. The report form authenticates the player through the existing Discord sign-in and creates a bug-ticket forum thread when the player explicitly submits it. Opening the form is not evidence of ticket creation; only the backend receipt confirms that.

The optional Export diagnostics action still writes a ZIP on the player's Desktop. It does not upload anything or open the obsolete Discord channel. The player is warned to review it before privately sharing with staff: crash dumps can contain private memory data despite text password redaction.

## Release order

Deploy and verify the coordinated Fairy Witch `/report/` form and `/api/public/bug-reports` backend before releasing this launcher change. Do not release a launcher pointing to a missing form. Backend delivery, authentication, idempotency and claim controls are covered in its own changeset. No bot token, webhook credential, session cookie or machine path is put in the launcher URL.

## Local verification

Run the launcher MSTest suite including BugReportSubmissionTests, BugReportTests and localization checks. No test should submit a real Discord report. The UI must clearly distinguish a form opened, a ticket submitted, and a local diagnostic export. Verify cancel/back, failed browser open, repeated form submission, expired Discord session, and failed export in release QA.
