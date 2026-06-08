namespace ChipLauncher.Services;

/// <summary>通知类型</summary>
public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>全局 UI 通知服务 — 任何页面可通过静态方法触发 Toast 提示</summary>
public static class AppNotification
{
    /// <summary>通知事件：消息内容 + 类型</summary>
    public static event Action<string, NotificationType>? OnShow;

    /// <summary>显示通知</summary>
    public static void Show(string message, NotificationType type = NotificationType.Info)
    {
        OnShow?.Invoke(message, type);
    }
}