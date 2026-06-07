using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ChipLauncher.Services;

/// <summary>
/// 日志级别
/// </summary>
public enum LogLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// 日志服务 - 同时输出到文件和控制台
/// </summary>
public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogFile;
    private static readonly object Lock = new();
    private static bool _consoleAttached;

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    static Logger()
    {
        LogDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "logs");

        if (!Directory.Exists(LogDir))
            Directory.CreateDirectory(LogDir);

        LogFile = Path.Combine(LogDir, $"log-{DateTime.Now:yyyy-MM-dd}.txt");
    }

    /// <summary>写入信息日志</summary>
    public static void Info(string message)
    {
        Write(LogLevel.Info, message);
    }

    /// <summary>写入警告日志</summary>
    public static void Warn(string message)
    {
        Write(LogLevel.Warning, message);
    }

    /// <summary>写入错误日志</summary>
    public static void Error(string message)
    {
        Write(LogLevel.Error, message);
    }

    /// <summary>写入错误日志（带异常）</summary>
    public static void Error(string message, Exception ex)
    {
        Write(LogLevel.Error, $"{message}{Environment.NewLine}{ex}");
    }

    private static void Write(LogLevel level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level.ToString().ToUpper()}] {message}";

        // 1. 输出到调试窗口（VS 输出窗口可见）
        Debug.WriteLine(line);

        // 3. 写入日志文件
        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
            catch
            {
                // 日志写入失败不抛异常
            }
        }
    }

    /// <summary>获取日志目录路径</summary>
    public static string GetLogDirectory() => LogDir;
}
