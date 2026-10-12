using System.IO;
using System.Windows;
using Microsoft.Win32;
using TranscriberClient.Models;
using TranscriberClient.Services;

namespace TranscriberClient.Views;

public partial class LocalAudioAssignmentWindow : Window
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".wma", ".m4a", ".aac", ".flac"
    };

    private readonly LocalAudioAssignmentService _assignmentService = new();
    private readonly UserAccount _currentUser;
    private readonly HashSet<int> _existingMachineNumbers;
    private string _sourcePath = string.Empty;
    public Record? CreatedRecord { get; private set; }

    public LocalAudioAssignmentWindow(UserAccount currentUser, IEnumerable<Record> existingRecords)
    {
        InitializeComponent();
        _currentUser = currentUser;
        _existingMachineNumbers = existingRecords
            .Select(record => record.MachineNum)
            .Where(machineNumber => machineNumber > 0)
            .ToHashSet();
        RecordedDateTextBox.Text = CalendarDateFormatter.FormatDate(DateTime.Today);
    }

    private void ChooseAudioButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a local court audio recording",
            Filter = "Supported audio files|*.wav;*.mp3;*.wma;*.m4a;*.aac;*.flac",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _sourcePath = dialog.FileName;
            AudioPathTextBox.Text = _sourcePath;
            ValidationText.Text = string.Empty;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_sourcePath) || !SupportedExtensions.Contains(Path.GetExtension(_sourcePath)))
        {
            ShowValidation("Choose an existing WAV, MP3, WMA, M4A, AAC, or FLAC recording.");
            return;
        }

        if (!int.TryParse(FileNumberTextBox.Text.Trim(), out var fileNumber) || fileNumber <= 0)
        {
            ShowValidation("Enter a valid court file number greater than zero.");
            FileNumberTextBox.Focus();
            return;
        }

        if (!int.TryParse(MachineNumberTextBox.Text.Trim(), out var machineNumber)
            || machineNumber is < 1 or > 999999)
        {
            ShowValidation("Enter a five- or six-digit machine number. A leading zero will be added to five-digit numbers.");
            MachineNumberTextBox.Focus();
            return;
        }

        if (_existingMachineNumbers.Contains(machineNumber))
        {
            ShowValidation($"Machine number {machineNumber:D6} already exists in your collection.");
            MachineNumberTextBox.Focus();
            return;
        }

        var applicant = ApplicantTextBox.Text.Trim();
        var defendant = DefendantTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(applicant) || string.IsNullOrWhiteSpace(defendant))
        {
            ShowValidation("Applicant and defendant are required for the local case record.");
            return;
        }

        if (!CalendarDateFormatter.TryParseDate(RecordedDateTextBox.Text, out var recordedDate))
        {
            ShowValidation("Enter a valid recorded date using the selected calendar format.");
            RecordedDateTextBox.Focus();
            return;
        }

        DateTime? appointmentDate = null;
        if (!string.IsNullOrWhiteSpace(AppointmentDateTextBox.Text))
        {
            if (!CalendarDateFormatter.TryParseDate(AppointmentDateTextBox.Text, out var parsedAppointment))
            {
                ShowValidation("Enter a valid appointment date using the selected calendar format, or leave it blank.");
                AppointmentDateTextBox.Focus();
                return;
            }

            appointmentDate = parsedAppointment;
            return;
        }

        try
        {
            CreatedRecord = await _assignmentService.CreateAsync(
                _sourcePath,
                fileNumber,
                machineNumber,
                applicant,
                defendant,
                WitnessTypeTextBox.Text.Trim(),
                WitnessesTextBox.Text.Trim(),
                TrialTextBox.Text.Trim(),
                JudgeTextBox.Text.Trim(),
                recordedDate,
                appointmentDate,
                RemarkTextBox.Text.Trim(),
                _currentUser.Username);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not create local audio assignment for {Username}", _currentUser.Username);
            ShowValidation($"The local audio assignment could not be saved. Check local disk permissions and available space.\n{ex.Message}");
        }
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
    }
}
