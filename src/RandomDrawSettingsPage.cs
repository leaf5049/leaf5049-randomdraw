using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;

namespace Leaf5049.RandomDraw;

/// <summary>
/// 插件专用设置页（设置 → 随机抽选）：控制名单、已抽中名单、滑窗大小等。
/// </summary>
[SettingsPageInfo("leaf5049.randomdraw.settings", "随机抽选", "\ue070", "\ue071", SettingsPageCategory.External)]
public class RandomDrawSettingsPage : SettingsPageBase
{
    private readonly DrawStore _store;
    private readonly TextBox _namesBox = new();
    private readonly TextBox _pickedBox = new();
    private readonly NumericUpDown _windowBox = new();
    private readonly TextBlock _statsText = new();
    private readonly TextBlock _statusText = new();

    public RandomDrawSettingsPage(DrawStore store)
    {
        _store = store;
        Content = BuildUi();
        LoadFromStore();

        // 抽选在别处发生时（比如按热键），页面跟着刷新，不用手动点「刷新」。
        _store.Changed += OnStoreChanged;
        Unloaded += (_, _) => _store.Changed -= OnStoreChanged;
    }

    private void OnStoreChanged(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

    private Control BuildUi()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };

        // —— 名单 ——
        panel.Children.Add(SectionTitle("名单", "一行一个姓名；空行和 # 开头的行会被忽略，重名只算一个。"));

        _namesBox.AcceptsReturn = true;
        _namesBox.Height = 220;
        _namesBox.Watermark = "张三" + Environment.NewLine + "李四";
        panel.Children.Add(_namesBox);

        panel.Children.Add(Wrap(
            Button("保存名单", () =>
            {
                var count = _store.SetNamesFromText(_namesBox.Text);
                LoadFromStore();
                SetStatus(count == 0
                    ? "已保存，但名单是空的：抽选时会提示名单为空。"
                    : $"已保存：全班 {count} 人，已同步写入 名单.txt 与 抽选数据.json。");
            }),
            Button("从 名单.txt 重新读取", () =>
            {
                _store.ReloadNamesFromFile();
                LoadFromStore();
                SetStatus($"已从 名单.txt 重新读取：全班 {_store.AllNames.Count} 人。");
            }),
            Button("打开 名单.txt", () => OpenPath(_store.NamesFilePath)),
            Button("打开插件文件夹", () => OpenPath(_store.FolderPath))
        ));

        // —— 已抽中 ——
        panel.Children.Add(SectionTitle("已抽中", "按抽取先后排列。撤销/清空后，对应同学立刻回到候选池。"));

        _pickedBox.IsReadOnly = true;
        _pickedBox.AcceptsReturn = true;
        _pickedBox.Height = 110;
        _pickedBox.Watermark = "还没有抽过";
        panel.Children.Add(_pickedBox);

        panel.Children.Add(Wrap(
            Button("撤销最后一位", () =>
            {
                var name = _store.RemoveLastPicked();
                Refresh();
                SetStatus(name == null ? "已抽中名单是空的，没有可撤销的记录。" : $"已撤销：{name}。");
            }),
            Button("清空已抽中", () =>
            {
                _store.ClearPicked();
                Refresh();
                SetStatus("已清空已抽中名单。");
            }),
            Button("刷新", () =>
            {
                LoadFromStore();
                SetStatus("已刷新。");
            })
        ));

        // —— 滑窗大小 ——
        panel.Children.Add(SectionTitle(
            "滑窗大小",
            "已抽中名单最多保留的人数。抽满后每抽一次就淘汰最早抽中的那位，保证同一窗口内不重复抽到同一个人。"));

        var windowRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _windowBox.Minimum = 1;
        _windowBox.Maximum = 500;
        _windowBox.Increment = 1;
        _windowBox.FormatString = "0";
        _windowBox.Width = 110;
        windowRow.Children.Add(_windowBox);
        windowRow.Children.Add(Button("保存滑窗大小", () =>
        {
            var value = (int)(_windowBox.Value ?? DrawStore.DefaultMaxPickedCount);
            var applied = _store.SetMaxPickedCount(value);
            _windowBox.Value = applied;
            Refresh();
            SetStatus($"滑窗大小已设为 {applied} 人"
                + (_store.AllNames.Count > applied
                    ? $"，候选池恒为 {Math.Max(0, _store.AllNames.Count - applied)} 人。"
                    : "，全班都在窗口内时会按“删最早”规则轮转。"));
        }));
        panel.Children.Add(windowRow);
        panel.Children.Add(Hint("例：全班 39 人、滑窗 24 → 候选池 15 人，被抽中者接下来 23 次抽选内不会再出现。"));

        // —— 汇总 ——
        _statsText.Opacity = 0.85;
        _statsText.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_statsText);
        _statusText.Opacity = 0.7;
        _statusText.TextWrapping = TextWrapping.Wrap;
        _statusText.FontSize = 12;
        panel.Children.Add(_statusText);
        panel.Children.Add(Hint(
            "数据文件：" + _store.DatabaseFilePath + Environment.NewLine +
            "名单文件：" + _store.NamesFilePath));

        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static Button Button(string text, Action onClick)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control Wrap(params Control[] children)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            wrap.Children.Add(child);
        }
        return wrap;
    }

    private static Control SectionTitle(string title, string description)
    {
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 12, 0, 0) };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 16 });
        if (description.Length > 0)
        {
            stack.Children.Add(Hint(description));
        }
        return new Border { Child = stack };
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        Opacity = 0.62,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap
    };

    private void LoadFromStore()
    {
        _namesBox.Text = string.Join(Environment.NewLine, _store.AllNames);
        _windowBox.Value = _store.MaxPickedCount;
        Refresh();
    }

    private void Refresh()
    {
        _pickedBox.Text = string.Join(Environment.NewLine, _store.PickedNames);
        var total = _store.AllNames.Count;
        var picked = _store.PickedNames.Count;
        var candidates = Math.Max(0, total - picked);
        _statsText.Text = $"全班 {total} 人  ·  滑窗 {_store.MaxPickedCount} 人  ·  已抽中 {picked} 人  ·  当前候选池 {candidates} 人";
    }

    private void SetStatus(string message) => _statusText.Text = message;

    private static void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch
        {
            // 打不开资源管理器不影响设置页使用，路径已经显示在页面上。
        }
    }
}
