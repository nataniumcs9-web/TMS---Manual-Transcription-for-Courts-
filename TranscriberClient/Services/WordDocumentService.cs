using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TranscriberClient.Models;

namespace TranscriberClient.Services;

public class WordDocumentService
{
    private readonly string _documentsFolder;
    private static object? _sharedWordApplication;
    private static readonly object WordApplicationLock = new();

    public WordDocumentService(string? documentsFolder = null)
    {
        _documentsFolder = documentsFolder ?? AppSettings.DocsFolder;
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                return Type.GetTypeFromProgID("Word.Application") != null;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Could not determine whether Microsoft Word is installed");
                return false;
            }
        }
    }

    private dynamic EnsureWordApplication()
    {
        lock (WordApplicationLock)
        {
            if (_sharedWordApplication != null)
            {
                try
                {
                    var active = (dynamic)_sharedWordApplication;
                    _ = active.Documents.Count;
                    return active;
                }
                catch (COMException)
                {
                    _sharedWordApplication = null;
                }
            }

            try
            {
                var wordType = Type.GetTypeFromProgID("Word.Application")
                    ?? throw new InvalidOperationException("Microsoft Word is not installed.");
                object wordApplication;
                try
                {
                    var classId = wordType.GUID;
                    GetActiveObject(ref classId, IntPtr.Zero, out wordApplication);
                }
                catch (COMException)
                {
                    wordApplication = Activator.CreateInstance(wordType)
                        ?? throw new InvalidOperationException("Could not start Microsoft Word.");
                    ((dynamic)wordApplication).Visible = false;
                }

                _sharedWordApplication = wordApplication;
                return (dynamic)wordApplication;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Microsoft Word could not be started or attached");
                throw new InvalidOperationException("Microsoft Word could not be started. Check the Office installation and try again.", ex);
            }
        }
    }

    public string GetDocumentPath(string fileNum, string machineNum)
    {
        var docxPath = Path.Combine(_documentsFolder, $"{machineNum}.docx");
        var docPath = Path.Combine(_documentsFolder, $"{machineNum}.doc");

        if (File.Exists(docxPath))
        {
            return docxPath;
        }

        if (File.Exists(docPath))
        {
            return docPath;
        }

        return docxPath;
    }

    public object? OpenOrCreateDocument(string fileNum, string machineNum, Record record)
    {
        Directory.CreateDirectory(_documentsFolder);
        var filePath = GetDocumentPath(fileNum, machineNum);

        if (!File.Exists(filePath))
        {
            MigrateLegacyDocument(fileNum, machineNum, filePath);
        }

        if (!File.Exists(filePath))
        {
            CreateDocxFile(filePath, fileNum, machineNum, record);
        }

        if ((File.GetAttributes(filePath) & FileAttributes.ReadOnly) != 0)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(filePath)}' is marked read-only in Windows. Remove its Read-only file attribute before editing.");
        }

        var existingDocument = TryGetOpenDocument(filePath);
        if (existingDocument != null)
        {
            EnsureEditable(existingDocument, filePath);
            ActivateDocument(existingDocument.Application, existingDocument);
            return existingDocument;
        }

        var process = Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
        if (process == null)
        {
            throw new InvalidOperationException($"Windows could not open '{filePath}' with its associated document editor.");
        }

        for (var attempt = 0; attempt < 24; attempt++)
        {
            var document = TryGetOpenDocument(filePath);
            if (document != null)
            {
                EnsureEditable(document, filePath);
                ActivateDocument(document.Application, document);
                return document;
            }

            Thread.Sleep(250);
        }

        Serilog.Log.Warning(
            "Word launched the transcription file but did not expose it for automation; file path remains {DocumentPath}",
            filePath);
        return null;
    }

    private void MigrateLegacyDocument(string fileNum, string machineNum, string targetPath)
    {
        var legacyStem = $"{fileNum}_{machineNum}";
        var legacyDocx = Path.Combine(_documentsFolder, $"{legacyStem}.docx");
        var legacyDoc = Path.Combine(_documentsFolder, $"{legacyStem}.doc");
        var legacyPath = File.Exists(legacyDocx) ? legacyDocx : File.Exists(legacyDoc) ? legacyDoc : null;
        if (legacyPath == null)
        {
            return;
        }

        var targetWithLegacyExtension = Path.ChangeExtension(targetPath, Path.GetExtension(legacyPath));
        if (File.Exists(targetWithLegacyExtension))
        {
            return;
        }

        File.Move(legacyPath, targetWithLegacyExtension);
        Serilog.Log.Information(
            "Migrated legacy transcription document {OldPath} to machine-number path {NewPath}",
            legacyPath,
            targetWithLegacyExtension);
    }

    private static dynamic? TryGetOpenDocument(string filePath)
    {
        try
        {
            var wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType == null)
            {
                return null;
            }

            var classId = wordType.GUID;
            GetActiveObject(ref classId, IntPtr.Zero, out var application);
            dynamic wordApplication = application;
            return FindOpenDocument(wordApplication.Documents, filePath);
        }
        catch (COMException ex)
        {
            Serilog.Log.Debug(ex, "Word automation is not ready for transcription file {DocumentPath}", filePath);
            return null;
        }
    }

    private static dynamic? FindOpenDocument(dynamic documents, string filePath)
    {
        var expectedPath = Path.GetFullPath(filePath);
        for (var index = 1; index <= (int)documents.Count; index++)
        {
            dynamic candidate = documents[index];
            try
            {
                if (string.Equals(
                    Path.GetFullPath((string)candidate.FullName),
                    expectedPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch (COMException ex)
            {
                Serilog.Log.Warning(ex, "Could not inspect an open Word document while looking for {DocumentPath}", expectedPath);
            }
        }

        return null;
    }

    public void OpenDocumentsFolder(string? documentPath = null)
    {
        Directory.CreateDirectory(_documentsFolder);
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = File.Exists(documentPath)
                ? $"/select,\"{Path.GetFullPath(documentPath!)}\""
                : $"\"{_documentsFolder}\"",
            UseShellExecute = true
        };

        if (Process.Start(startInfo) == null)
        {
            throw new InvalidOperationException("Windows Explorer did not start.");
        }
    }

    public object CreateReplacementDocument(string fileNum, string machineNum, Record record, out string backupPath)
    {
        Directory.CreateDirectory(_documentsFolder);
        var docxPath = Path.Combine(_documentsFolder, $"{fileNum}_{machineNum}.docx");
        var docPath = Path.Combine(_documentsFolder, $"{fileNum}_{machineNum}.doc");
        var existingPath = File.Exists(docxPath) ? docxPath : File.Exists(docPath) ? docPath : null;
        if (existingPath == null)
        {
            throw new FileNotFoundException("The original transcription file no longer exists.");
        }

        var token = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        backupPath = Path.Combine(_documentsFolder, $"{Path.GetFileNameWithoutExtension(existingPath)}.corrupt-{token}{Path.GetExtension(existingPath)}");
        var temporaryPath = Path.Combine(_documentsFolder, $"{fileNum}_{machineNum}.recovery-{token}.docx");
        var wordApplication = EnsureWordApplication();

        try
        {
            dynamic replacement = CreateDocument(wordApplication, temporaryPath, fileNum, machineNum, record);
            replacement.Close(0);

            File.Move(existingPath, backupPath);
            try
            {
                File.Move(temporaryPath, docxPath);
            }
            catch
            {
                File.Move(backupPath, existingPath);
                throw;
            }

            dynamic recovered = OpenWordDocument(wordApplication.Documents, docxPath);
            EnsureEditable(recovered, docxPath);
            recovered.Save();
            ActivateDocument(wordApplication, recovered);
            return recovered;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not recover transcription document {DocumentPath}", existingPath);
            throw new InvalidOperationException("Word could not create an editable replacement document.", ex);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void InsertTimestamp(object documentReference, TimeSpan timestamp)
    {
        var stamp = $"[{timestamp:mm\\:ss}] ";
        dynamic document = documentReference;
        var selection = document.Application.Selection;
        selection.TypeText(stamp);
        selection.EndKey(6, 0);
        selection.TypeParagraph();
    }

    public void BringWordToFront(object? documentReference)
    {
        if (documentReference == null)
        {
            throw new InvalidOperationException("Open the transcription document first.");
        }

        try
        {
            dynamic document = documentReference;
            var wordApplication = document.Application;
            ActivateDocument(wordApplication, document);
        }
        catch (COMException ex)
        {
            Serilog.Log.Error(ex, "The Word document window could not be activated");
            throw new InvalidOperationException(
                "Word could not activate the transcription document. Close other Word dialogs and reopen the document.",
                ex);
        }
    }

    public void SaveDocument(object? documentReference)
    {
        if (documentReference == null)
        {
            return;
        }

        try
        {
            dynamic document = documentReference;
            document.Save();
            if (!document.Saved)
            {
                throw new IOException("Word did not confirm that the document was saved.");
            }

        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not save the Word document");
            throw;
        }
    }

    private static dynamic OpenWordDocument(dynamic documents, string filePath)
    {
        dynamic document = FindOpenDocument(documents, filePath)
            ?? documents.Open(Path.GetFullPath(filePath), false, false, false, Type.Missing, Type.Missing, false,
                Type.Missing, Type.Missing, Type.Missing, Type.Missing, true, true, Type.Missing, true, Type.Missing);
        if (document == null)
        {
            throw new InvalidOperationException($"Word could not open '{Path.GetFileName(filePath)}'.");
        }

        return document;
    }

    private static dynamic CreateDocument(dynamic wordApplication, string filePath, string fileNum, string machineNum, Record record)
    {
        if (!File.Exists(filePath))
        {
            CreateDocxFile(filePath, fileNum, machineNum, record);
        }

        var document = OpenWordDocument(wordApplication.Documents, filePath);
        EnsureEditable(document, filePath);
        ActivateDocument(wordApplication, document);
        return document;
    }

    private static void CreateDocxFile(string filePath, string fileNum, string machineNum, Record record)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(filePath)!,
            $"{Path.GetFileNameWithoutExtension(filePath)}.{Guid.NewGuid():N}.tmp");
        var paragraphs = new[]
        {
            $"Transcription for: {fileNum}_{machineNum}",
            $"Recorder: {record.Recorder}",
            $"Date: {record.RecDate:yyyy-MM-dd}",
            $"Applicant: {record.Applicant}",
            $"Defendant: {record.Defendant}",
            $"Trial: {record.Trial}",
            $"Judge: {record.Judge}",
            $"Witness Type: {record.WitnessType}",
            "Witnesses:",
            record.Witnesses,
            string.Empty
        };

        try
        {
            using (var package = WordprocessingDocument.Create(temporaryPath, WordprocessingDocumentType.Document))
            {
                var mainPart = package.AddMainDocumentPart();
                mainPart.Document = new Document(
                    new Body(paragraphs.Select(text =>
                        new Paragraph(new Run(new Text(text ?? string.Empty)
                        {
                            Space = SpaceProcessingModeValues.Preserve
                        })))));
                mainPart.Document.Save();
            }

            if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
            {
                throw new IOException($"Could not create the transcription file at '{filePath}'.");
            }

            File.Move(temporaryPath, filePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureEditable(dynamic document, string filePath)
    {
        if (document.ReadOnly)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(filePath)}' opened as read-only. Close other Word windows using this file and check file permissions.");
        }

        if ((int)document.ProtectionType != -1)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(filePath)}' is protected from editing in Word. Remove document protection before transcription.");
        }
    }

    private static void ActivateDocument(object? wordApplicationReference, object? documentReference)
    {
        if (wordApplicationReference is null || documentReference is null)
        {
            return;
        }

        dynamic wordApplication = wordApplicationReference;
        dynamic document = documentReference;
        try
        {
            wordApplication.Visible = true;
            wordApplication.Activate();
            document.Activate();
            dynamic activeWindow = document.ActiveWindow;
            if (activeWindow == null)
            {
                throw new InvalidOperationException("Word did not provide a document window.");
            }

            activeWindow.Visible = true;
            activeWindow.WindowState = 1;
            activeWindow.Activate();
            document.Application.Selection.EndKey(6, 0);
            var handle = new IntPtr(activeWindow.Hwnd);
            ShowWindow(handle, 9);
            if (!SetForegroundWindow(handle))
            {
                Serilog.Log.Warning("Windows did not grant foreground focus to Word window {WindowHandle}", handle);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Word document was opened, but Windows did not confirm foreground activation");
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.Interface)]
    private static extern void GetActiveObject(
        ref Guid classId,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.Interface)] out object activeObject);

    private static void CloseWithoutSaving(object? documentReference)
    {
        if (documentReference == null)
        {
            return;
        }

        try
        {
            dynamic document = documentReference;
            document.Close(0);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Could not close an unsuccessful Word document open");
        }
    }

}
