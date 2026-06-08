using System.Diagnostics;
using System.IO;
using System.Text;

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
/// 日志服务 - 输出到文件 + IDE 控制台（UTF-8 直写，无乱码）
/// </summary>
public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogFile;
    private static readonly object Lock = new();
    private static readonly Stream? StdoutStream;

    static Logger()
    {
        LogDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "logs");

        if (!Directory.Exists(LogDir))
            Directory.CreateDirectory(LogDir);

        LogFile = Path.Combine(LogDir, $"{DateTime.Now:yy-MM-dd.HH:mm:ss}.txt");

        // 打开 stdout 原始字节流，直接写入 UTF-8 绕过编码问题
        try
        {
            StdoutStream = Console.OpenStandardOutput();
        }
        catch
        {
            StdoutStream = null;
        }
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

        // 1. 调试输出（VS / Rider 输出窗口）
        Debug.WriteLine(line);

        // 2. IDE 控制台（直接写入 UTF-8 字节流，彻底避免编码问题）
        if (StdoutStream != null)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                StdoutStream.Write(bytes, 0, bytes.Length);
                StdoutStream.Flush();
            }
            catch
            {
                // 忽略
            }
        }

        // 3. 写入日志文件
        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 忽略
            }
        }
    }

    /// <summary>获取日志目录路径</summary>
    public static string GetLogDirectory() => LogDir;
}
