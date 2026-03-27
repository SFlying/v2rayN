namespace ServiceLib.Models;
public class AvailabilityTarget
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DestinationName { get; set; } // 比如 Gemini, Grok
    public string TestUrl { get; set; }        // 检测地址
    public bool IsEnabled { get; set; } = true;
    public string SuccessKeywords { get; set; } // 关键特征码
    public string UserAgent { get; set; }       // 自定义 UA
    public int Sort { get; set; }
}
