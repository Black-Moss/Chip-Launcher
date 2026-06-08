using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using ChipLauncher.Models;

namespace ChipLauncher.Views;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();

        // 从 .ico 中提取最大分辨率帧显示
        var iconPath = Path.Combine(AppContext.BaseDirectory, "ChipLauncher.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                AppIcon.Source = LoadLargestIconFrame(iconPath);
            }
            catch
            {
                // 忽略，使用默认样式
            }
        }

        var info = new LauncherInfo();
        VersionText.Text = $"v{info.Version}";

        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        BuildInfoText.Text = version != null
            ? $"Build {version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : "";
    }

    private void OnLinkClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: string url } && !string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>
    ///     从 .ico 文件中提取最大尺寸的 PNG 帧作为 Bitmap
    /// </summary>
    private static Bitmap LoadLargestIconFrame(string icoPath)
    {
        using var stream = File.OpenRead(icoPath);
        using var reader = new BinaryReader(stream);

        // ICO 文件头
        reader.ReadUInt16(); // reserved
        reader.ReadUInt16(); // reserved
        var count = reader.ReadUInt16();

        // 读取目录项，找到最大的帧
        var entries = new (byte Width, byte Height, uint Size, uint Offset)[count];
        for (var i = 0; i < count; i++)
        {
            var w = reader.ReadByte();     // 0 = 256
            var h = reader.ReadByte();     // 0 = 256
            reader.ReadByte();             // color palette count
            reader.ReadByte();             // reserved
            reader.ReadUInt16();           // planes / color depth
            reader.ReadUInt16();           // bits per pixel
            var size = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            entries[i] = (w, h, size, offset);
        }

        // 按尺寸降序排列，取最大帧
        var best = entries
            .OrderByDescending(e => (e.Width == 0 ? 256 : e.Width) * (e.Height == 0 ? 256 : e.Height))
            .First();

        // 读取该帧的图像数据
        reader.BaseStream.Seek(best.Offset, SeekOrigin.Begin);
        var imageData = reader.ReadBytes((int)best.Size);

        // 现代 .ico 的大尺寸帧通常是 PNG 格式
        using var ms = new MemoryStream(imageData);
        return new Bitmap(ms);
    }
}
