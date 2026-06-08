namespace ChipLauncher.Models;

/// <summary>
///     游戏信息模型
/// </summary>
public class GameInfo
{
    /// <summary>Steam AppId</summary>
    public string AppId { get; set; } = "4576490";

    /// <summary>游戏名称</summary>
    public string Name { get; set; } = "Casualties Unknown Demo";

    /// <summary>本地可执行文件路径（备用启动方式）</summary>
    public string? ExecutablePath { get; set; }
}