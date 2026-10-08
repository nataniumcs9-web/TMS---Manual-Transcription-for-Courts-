# TMS Desktop Transcriber Client

This project is a WPF desktop client for the Gedeo Zone High Court transcription workflow. It replaces the older PHP-based interface with a small always-on-top desktop application that allows a transcriber to log in, review assigned tasks, play audio, maintain Word transcription files, save progress, and use a Philips ACC2330/00 foot pedal.

## Requirements

- Windows 10/11
- .NET 8 SDK
- Microsoft Word 2016+ installed on the machine
- MySQL database reachable on the court network with the `tms` database
- Philips ACC2330/00 foot pedal connected via USB if the pedal is to be used

## Project Structure

- `TranscriberClient/` – WPF application and code-behind
- `Database/schema.sql` – database schema script

## Configuration

The audio base URL can be set in `TranscriberClient/appsettings.json`. Transcription documents are stored in the current Windows user's `Documents\GZHC_Files` folder. The database settings can be configured from the **Database Settings...** button on the login window before signing in.

```json
{
  "Database": {
    "ConnectionString": "Server=192.168.0.14;Database=tms;Uid=root;Pwd=;"
  },
  "Audio": {
    "BaseUrl": "http://192.168.0.14/TMS/Recorder/"
  },
  "Logging": {
    "LogFile": "logs/transcriber-.log"
  }
}
```

## Standard installation and first-time setup

### 1. Prepare the workstation

- Use a supported Windows 10/11 x64 workstation.
- Install Microsoft Word 2016 or later; Word is required for the transcription document workflow.
- Connect the MySQL server over the court's trusted network. Obtain the database server/IP, port (normally `3306`), database name, and a dedicated database-service account from the database administrator.
- Each transcriber needs a Windows profile with enough local disk space for locally created audio assignments.

### 2. Prepare and verify the database

- Back up the database before making schema changes.
- For a new installation, run `Database/schema.sql` against the intended MySQL database and confirm the `records`, `audio_progress`, `req_acc`, and `files` tables exist.
- For an existing TMS database, preserve existing data and have the database administrator compare its schema with `Database/schema.sql`; apply only missing, reviewed changes. Do not drop/recreate production tables.
- The database account used by the desktop app needs `SELECT` access to `req_acc` and `records`, `SELECT`/`INSERT`/`UPDATE` access to `audio_progress`, and `UPDATE` access to `records`. Transcriber password changes require `UPDATE` access to `req_acc.password`.
- Ensure each person who will sign in has a `req_acc` row with their own username, `status` set to `Active`, `role` set to `Transcriber`, and a BCrypt password hash in `password`. The database-service account and its password are for the connection only; they are not the transcriber sign-in credentials.

### 3. Install the desktop application

- Run `installer\output\GZHC-Transcriber-Setup-1.0.2.exe` for the standard Windows installation wizard. It shows the install folder, Start Menu group, shortcut choices, install progress, and a finish page with an optional launch checkbox. Setup registers the app in Windows' installed-apps/uninstall list and creates an uninstaller; the default is a per-user install under `%LOCALAPPDATA%\Programs`, so it does not require administrator rights. The installer can be uninstalled or modified from Windows **Installed apps** / **Programs and Features**.
- To create that installer from source, install the .NET 8 SDK and Inno Setup 6, then run `installer\build-installer.ps1` from PowerShell in the repository. The self-contained application does not need a separate .NET runtime on the target workstation.
- Alternatively, distribute and run `TranscriberClient\publish\win-x64\TranscriberClient.exe`. Keep that executable with its published support files/folders as a release payload; do not copy only the executable out of the publish folder.
- Do not distribute a development build from `bin\Debug`.

### 4. Configure the database connection

1. Launch **GZHC Court Transcriber**.
2. On the sign-in screen, select **Database connection settings**.
3. Enter the database server/IP, port, database name, database-service username, and its password.
4. Select **Test Connection**. A successful result confirms MySQL connectivity and that the `req_acc` login table/columns can be read. It does **not** check a transcriber's username/password.
5. Select **Save**. Settings are saved for the current Windows user under `%LOCALAPPDATA%\TranscriberClient\database.json`; the database password is protected using Windows DPAPI. Configure and save settings separately for each Windows user/workstation.

### 5. Sign in and configure user preferences

- Sign in with the individual transcriber username and password belonging to an Active `Transcriber` account in `req_acc`. Do not enter the database-service username/password on the sign-in screen.
- After a successful online sign-in, the app stores a DPAPI-protected credential verifier for this Windows user. It allows offline sign-in for up to 30 days on this Windows account; it does not store the plaintext password. Offline sign-in cannot check whether the server account was subsequently disabled or its password changed elsewhere.
- Open **User settings** to set automatic refresh (15 seconds to 5 minutes), choose compact assignment rows, or change your transcriber password. Password changes require the current password and the database account needs permission to update `req_acc.password`.
- Search filters loaded assignments immediately. The dashboard checks the server automatically; it retries after temporary connection errors.
- Use **Add local audio** to choose an audio file from the workstation and create a local case assignment. The file is copied into `%LOCALAPPDATA%\TranscriberClient\Audio` and its required case details are saved in a matching `.tms.json` sidecar. This workflow does not require a server connection and is visible only to the same Windows user on that workstation; it does not create a database assignment.
- Open **Reports** to filter by date/status, review workload and appointment charts with summary metrics, and export Excel or PDF. Excel contains summary/chart data and assignment details; PDF includes report totals and chart summaries. For PDF output, select **Microsoft Print to PDF** in the Windows print dialog.
- Appointment-date reminders are workflow recommendations; they are not a substitute for a court-set deadline.

### Troubleshooting sign-in

- **Database connection test fails:** verify server/IP, port, database name, network/firewall access, and the database-service account/password. Ask the database administrator to check MySQL grants.
- **Test succeeds but sign-in fails:** connection settings and transcriber sign-in are separate. Use the transcriber's own account and ask the administrator to verify the matching `req_acc.username`, `status = 'Active'`, `role = 'Transcriber'`, and a valid BCrypt hash in `password`.
- **Sign-in reports an account/table error:** confirm the `req_acc` table and expected columns exist and the database-service account has `SELECT` permission.
- **Offline sign-in is unavailable:** sign in online once with the transcriber account on that Windows user profile. Offline sign-in requires the cached verifier to be less than 30 days old and does not synchronize remote assignments.
- **Local audio is unavailable on another PC or Windows account:** local assignments and audio are intentionally stored in the creating profile's `%LOCALAPPDATA%` folder. They do not synchronize with the server.
- Application logs are written under the configured log path (by default, `logs` beside the application). Never send database passwords in logs, screenshots, or support messages.

## Application Notes

- The app uses the recorder's MySQL `records` table and loads files from the configured audio base URL.
- Saved database settings are stored per Windows user under `%LOCALAPPDATA%\TranscriberClient\database.json`; the database password is protected with Windows DPAPI and is available to that same Windows user on future runs.
- Starting work changes the record status to Pending and opens the single `{machine number}.docx` in `Documents\GZHC_Files`, creating it only if it does not exist. Existing `{file number}_{machine number}` documents are migrated to the machine-number filename. Continue opens the same saved file; Word is opened through Windows file association and the app attaches only to that exact path. Use **Show in Explorer** to locate it quickly.
- The dashboard's always-on-top preference defaults off; the transcription mini-window preference defaults on. Each choice is saved for the current Windows user and restored next time.
- The dashboard highlights assigned and in-progress work, counts active records whose appointment dates have passed or are today, and recommends files to review using the appointment date stored in the database. These are appointment reminders, not a replacement for a court-set transcript deadline.
- **Add local audio** accepts WAV, MP3, WMA, M4A, AAC, or FLAC from a local folder. It copies the recording and saves required case details, status, and playback position in a `.tms.json` sidecar next to the copied audio under `%LOCALAPPDATA%\TranscriberClient\Audio`. Local assignments and changes remain on that Windows user profile; server assignments continue to use the existing recorder audio URL and database workflow.
- Dashboard search covers record/file/machine numbers, names, witness details, recorder, judge, dates, status, audio details, and notes. It filters cached results as you type; automatic server refresh is configurable in **User settings**.
- **Reports** filters the current transcriber's loaded work by date and status, displays KPI summaries and status/appointment charts, and exports those summaries with assignment details to Excel or PDF. PDF uses the Windows print-to-PDF workflow.
- Closing a transcription task saves its Word document but leaves the document open in Word. Continuing the task reconnects to the same open file when possible.
- Server audio progress is saved to the `audio_progress` table keyed by `machine_num`; local audio progress is saved in the adjacent sidecar file.
- Offline sign-in uses a DPAPI-protected verifier cached after successful online authentication. It is tied to the current Windows user, expires after 30 days, and cannot validate remote account suspension or password changes while disconnected. Local-only assignments remain local and are not synchronized.
- The foot pedal uses HID vendor ID `0x0911` and listens for the mapped byte pattern: `0x04` rewind, `0x02` play/pause, `0x01` forward.
- The app can run with a small always-on-top window while another application, such as Word, is active.
- Audio playback position is persisted to MySQL every five seconds while audio is playing, and immediately when playback is paused or the task is closed.
- The project-root `logo.png` is embedded into the app and shown on the login, dashboard, and transcription workspace. The login screen uses subtle animated branding instead of a separate splash window.
- **User settings → System → Calendar display** switches displayed and entered dates between Gregorian and Ethiopian calendars. Dates are converted for display and entry only; database dates continue to be stored and queried as Gregorian. Report and local-assignment date fields accept ISO `yyyy-MM-dd` in Gregorian mode and `d MonthName yyyy` (for example, `1 Meskerem 2019`) or `yyyy-MM-dd` in Ethiopian mode.
- Developer contact: Natnael Abebe · [nataniumcs9@gmail.com](mailto:nataniumcs9@gmail.com).

## Security and Deployment

- Never put database passwords in source code, committed configuration, logs, or support screenshots. Enter the database-service password in **Database connection settings**; the desktop app stores it per Windows user protected with DPAPI.
- Use a dedicated database-service account with only the privileges listed in the installation procedure, and use separate individual transcriber accounts for sign-in.
- Ensure that Word automation and the MySQL database can be accessed on the target workstation.
- If the system is used in an enterprise environment, restrict the application folder permissions and run with the minimum required privileges.
