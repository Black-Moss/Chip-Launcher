namespace ChipLauncher.Services;

/// <summary>
/// 游戏启动服务接口
/// </summary>
public interface IGameService
{
    /// <summary>通过 Steam 协议启动游戏</summary>
    void LaunchViaSteam();

    /// <summary>直接启动本地可执行文件</summary>
    void LaunchDirectly(string executablePath);
}
