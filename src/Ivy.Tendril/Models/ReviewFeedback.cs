namespace Ivy.Tendril.Models;

public record ReviewCommentItem
{
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
    public string Content { get; set; } = "";
}

public record ReviewAnnotationItem
{
    public string SelectedText { get; set; } = "";
    public string Comment { get; set; } = "";
}

public record ReviewFeedback
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Author { get; set; } = "";
    public string PlanFolder { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Summary { get; set; } = "";
    public List<ReviewCommentItem> DiffComments { get; set; } = [];
    public List<ReviewAnnotationItem> Annotations { get; set; } = [];
}
