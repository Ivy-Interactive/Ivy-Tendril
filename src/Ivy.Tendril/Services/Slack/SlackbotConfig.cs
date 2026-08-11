namespace Ivy.Tendril.Services.Slack;

public record SlackbotConfig
{
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = "";
    public string AppToken { get; set; } = "";
    public string Project { get; set; } = "Auto";
    public int HistoryLimit { get; set; } = 15;
}
