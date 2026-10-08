# TMS Desktop Transcriber Client

This project is a WPF desktop client for the Gedeo Zone High Court transcription workflow. It replaces the older PHP-based interface with a small always-on-top desktop application that allows a transcriber to log in, review assigned tasks, play audio, maintain Word transcription files, save progress, and use a Philips ACC2330/00 foot pedal.

## Requirements

- Windows 10/11
- .NET 8 SDK
- Microsoft Word 2016+ installed on the machine
- MySQL database running on `192.168.0.14` with the `tms` database
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

## Setup

1. Install the .NET 8 SDK.
2. Run the database schema script against your MySQL `tms` database.
3. Ensure the `req_acc` table contains an active transcriber account with a BCrypt password hash.
4. Launch the app and open **Database Settings...**. Enter the MySQL server/IP, port, database name, database username, and password, then select **Test Connection**. Save settings after the test succeeds.
5. Restore package dependencies:
   ```powershell
   dotnet restore
   ```
6. Build the project:
   ```powershell
   dotnet build TranscriberClient.sln
   ```
7. Run the desktop application:
   ```powershell
   dotnet run --project TranscriberClient\TranscriberClient.csproj
   ```

## Windows Installer

The project icon is generated from the court logo and embedded in the application executable. To build a self-contained x64 installer, install Inno Setup 6 and run `installer\build-installer.ps1`. The installer wizard lets each user select the install directory and choose Start Menu and desktop shortcuts; it also provides uninstall support and can launch the app after installation. The installed application does not require the .NET runtime, but Microsoft Word is required for Word automation.

## Application Notes

- The app uses the recorder's MySQL `records` table and loads files from the configured audio base URL.
- Saved database settings are stored per Windows user under `%LOCALAPPDATA%\TranscriberClient\database.json`; the database password is protected with Windows DPAPI and is available to that same Windows user on future runs.
- Starting work changes the record status to Pending and opens the single `{machine number}.docx` in `Documents\GZHC_Files`, creating it only if it does not exist. Existing `{file number}_{machine number}` documents are migrated to the machine-number filename. Continue opens the same saved file; Word is opened through Windows file association and the app attaches only to that exact path. Use **Show in Explorer** to locate it quickly.
- The dashboard's always-on-top preference defaults off; the transcription mini-window preference defaults on. Each choice is saved for the current Windows user and restored next time.
- Closing a transcription task saves its Word document but leaves the document open in Word. Continuing the task reconnects to the same open file when possible.
- Audio progress is saved to the `audio_progress` table keyed by `machine_num`.
- The foot pedal uses HID vendor ID `0x0911` and listens for the mapped byte pattern: `0x04` rewind, `0x02` play/pause, `0x01` forward.
- The app can run with a small always-on-top window while another application, such as Word, is active.
- Audio playback position is persisted to MySQL every five seconds while audio is playing, and immediately when playback is paused or the task is closed.
- The project-root `logo.png` is embedded into the app and shown on the splash, login, dashboard, and transcription workspace.
- Startup displays a branded Gedeo Zone High Court splash screen in English and Amharic before opening the login window.

## Security and Deployment

- Do not store passwords in source code. Keep strong credentials in the database and config files.
- Ensure that Word automation and the MySQL database can be accessed on the target workstation.
- If the system is used in an enterprise environment, restrict the application folder permissions and run with the minimum required privileges.
