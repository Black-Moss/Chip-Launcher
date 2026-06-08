using System.Diagnostics;

namespace ChipLauncher.Services;

/// <summary>
/// 游戏启动服务实现
/// </summary>
public class GameService : IGameService
{
    public void LaunchViaSteam()
    {
        const string uri = $"steam://rungameid/4576510";
        Logger.Info($"通过 Steam 启动游戏: URI={uri}");

        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true,
        });
    }

    public void LaunchDirectly(string executablePath)
    {
        Logger.Info($"直接启动游戏: Path={executablePath}");

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
        });
    }
}
