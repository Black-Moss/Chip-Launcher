using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ChipLauncher.Models;

namespace ChipLauncher.Views;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();

        var info = new LauncherInfo();
        VersionText.Text = $"v{info.Version}";

        // 显示构建版本号
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        BuildInfoText.Text = version != null
            ? $"Build {version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : "";

        // 加载窗口图标
        LoadAppIcon();
    }

    /// <summary>从嵌入资源加载 ICO 文件并显示在关于页</summary>
    private void LoadAppIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            // 嵌入资源名为: {DefaultNamespace}.{FileName}
            // 项目默认命名空间为 ChipLauncher，文件名为 ChipLauncher.ico
            const string resourceName = "ChipLauncher.ChipLauncher.ico";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return;

            var data = new byte[stream.Length];
            var read = stream.Read(data, 0, data.Length);
            if (read == 0) return;

            // ICO 文件格式:
            //   字节 0-1: 保留 (0)
            //   字节 2-3: 类型 (1 = ICO)
            //   字节 4-5: 图像数量
            //   之后每个目录项 16 字节:
            //     字节 0-3: 宽, 高, 颜色数, 保留
            //     字节 4-5: 颜色平面
            //     字节 6-7: 位深
            //     字节 8-11: 图像数据大小 (LittleEndian int32)
            //     字节 12-15: 图像数据偏移量 (LittleEndian int32)
            var count = BitConverter.ToInt16(data, 4);
            if (count == 0) return;

            // 取第一个条目的数据偏移和大小
            var imageSize = BitConverter.ToInt32(data, 14);
            var imageOffset = BitConverter.ToInt32(data, 18);
            if (imageOffset + imageSize > data.Length) return;

            // ICO 内嵌的图像可能是 PNG 或 BMP DIB。尝试直接解码，
            // 若失败则直接用 WriteableBitmap 写入原始 BGRA 像素（保留 alpha）。
            using (var ms = new MemoryStream(data, imageOffset, imageSize))
            {
                try
                {
                    AppIcon.Source = new Bitmap(ms);
                    return;
                }
                catch
                {
                    // 尝试原始像素方式
                }
            }

            // 解析 BITMAPINFOHEADER
            var biSize = BitConverter.ToInt32(data, imageOffset); // 通常 40
            var biWidth = BitConverter.ToInt32(data, imageOffset + 4);
            var biHeight = BitConverter.ToInt32(data, imageOffset + 8); // ICO 中翻倍
            var biBitCount = BitConverter.ToInt16(data, imageOffset + 14);

            var actualHeight = biHeight / 2;
            var rowSize = (biWidth * biBitCount + 31) / 32 * 4; // 4 字节对齐行宽
            var pixelDataSize = rowSize * actualHeight; // 仅 XOR 像素，不含 AND mask

            // 直接用 WriteableBitmap 写入原始 BGRA 像素，保留 alpha 通道
            var writeableBitmap = new WriteableBitmap(
                new PixelSize(biWidth, actualHeight),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using (var locked = writeableBitmap.Lock())
            {
                Marshal.Copy(
                    data, imageOffset + biSize, locked.Address, pixelDataSize);
            }

            AppIcon.Source = writeableBitmap;
        }
        catch
        {
            // 忽略图标加载失败
        }
    }

    private void OnLinkClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: string url } && !string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}