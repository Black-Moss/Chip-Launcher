namespace ChipLauncher.Models;

/// <summary>
/// 游戏信息模型
/// </summary>
public class GameInfo
{
    /// <summary>Steam AppId</summary>
    public string AppId { get; set; } = "730";

    /// <summary>游戏名称</summary>
    public string Name { get; set; } = "Counter-Strike 2";

    /// <summary>本地可执行文件路径（备用启动方式）</summary>
    public string? ExecutablePath { get; set; }
}
