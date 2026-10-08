namespace TranscriberClient.Models;

public class AudioProgress
{
    public int Id { get; set; }
    public string MachineNum { get; set; } = string.Empty;
    public string LastPosition { get; set; } = "0";
}
