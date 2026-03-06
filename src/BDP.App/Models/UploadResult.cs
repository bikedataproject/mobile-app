namespace BDP.App.Models;

public sealed class UploadResult
{
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = [];
}
