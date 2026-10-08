using System.Text.Json.Serialization;

namespace TranscriberClient.Models;

public class Record
{
    public int Id { get; set; }
    public DateTime? RecDate { get; set; }
    public int FileNum { get; set; }
    public int MachineNum { get; set; }
    public string Applicant { get; set; } = string.Empty;
    public string Defendant { get; set; } = string.Empty;
    public string WitnessType { get; set; } = string.Empty;
    public string Witnesses { get; set; } = string.Empty;
    public string Trial { get; set; } = string.Empty;
    public string Judge { get; set; } = string.Empty;
    public string Audio { get; set; } = string.Empty;
    public string AudioStatus { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public DateTime? AppointedOn { get; set; }
    public DateTime? InsertedOn { get; set; }
    public string Recorder { get; set; } = string.Empty;
    public string Transcriber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DistributedOn { get; set; }
    public DateTime? FinishedDate { get; set; }
    public string Doc { get; set; } = string.Empty;
    public double AudioPositionSeconds { get; set; }
    [JsonIgnore]
    public bool IsLocalOnly { get; set; }
}
