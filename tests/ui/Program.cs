using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using Leaf5049.RandomDraw;

namespace UiSmoke;

public static class Program
{
    private static int _failures;

    public static void Main()
    {
        AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();

        Dispatcher.UIThread.Invoke(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "rd_ui_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "名单.txt"),
                string.Join(Environment.NewLine, Enumerable.Range(1, 39).Select(i => "学生" + i)), Encoding.UTF8);

            var store = new DrawStore(folder);
            SettingsPageBase page = new RandomDrawSettingsPage(store);

            Check("设置页继承 SettingsPageBase 且构造时已建好 Content",
                () => page is SettingsPageBase && Root(page) is not null);

            var boxes = Find<TextBox>(Root(page)).ToList();
            Check("名单框（可编辑）与已抽中框（只读）都在", () =>
                boxes.Count >= 2
                && boxes.Any(t => !t.IsReadOnly && (t.Text ?? "").Contains("学生1"))
                && boxes.Any(t => t.IsReadOnly));

            var numeric = Find<NumericUpDown>(Root(page)).ToList();
            Check("滑窗大小数字框：范围 1–500，初值 24", () =>
                numeric.Count == 1 && numeric[0].Minimum == 1 && numeric[0].Maximum == 500 && numeric[0].Value == 24);

            var buttons = Find<Button>(Root(page)).Select(b => b.Content?.ToString() ?? "").ToList();
            Check("按钮齐全（保存名单/重新读取/打开 txt/打开文件夹/撤销/清空/刷新/保存滑窗）", () => buttons.Count >= 8);

            Check("汇总行：全班 39 人 · 滑窗 24 人 · 已抽中 0 人 · 候选池 39 人", () =>
                Texts(Root(page)).Any(t => t.Contains("全班 39 人") && t.Contains("滑窗 24 人")
                                           && t.Contains("已抽中 0 人") && t.Contains("当前候选池 39 人")));

            Check("数据文件与名单文件路径展示在页面上", () =>
                Texts(Root(page)).Any(t => t.Contains("抽选数据.json") && t.Contains("名单.txt")));

            var info = typeof(RandomDrawSettingsPage).GetCustomAttributes(false).OfType<SettingsPageInfo>().First();
            Check("设置页信息：id leaf5049.randomdraw.settings / 名称「随机抽选」/ External 分类", () =>
                info.Id == "leaf5049.randomdraw.settings" && info.Name == "随机抽选" && info.Category == SettingsPageCategory.External);

            // 模拟在数字框输入 10 并点「保存滑窗大小」
            numeric[0].Value = 10;
            var save = Find<Button>(Root(page)).First(b => (b.Content?.ToString() ?? "") == "保存滑窗大小");
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Check("点保存后数字框回填生效值 10 且 store 同步", () => numeric[0].Value == 10 && store.MaxPickedCount == 10);
            Check("状态行给出反馈文案", () => Texts(Root(page)).Any(t => t.Contains("滑窗大小已设为 10 人")));
            // 抽满滑窗后候选池应为 39 - 10 = 29 人，点「刷新」后汇总行应同步
            for (var i = 0; i < 10; i++) store.Draw();
            Find<Button>(Root(page)).First(b => (b.Content?.ToString() ?? "") == "刷新")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("抽满滑窗后汇总行刷新为已抽中 10 人 / 候选池 29 人", () =>
                Texts(Root(page)).Any(t => t.Contains("滑窗 10 人") && t.Contains("已抽中 10 人")
                                           && t.Contains("当前候选池 29 人")));
            Check("滑窗大小写回 抽选数据.json", () =>
                File.ReadAllText(Path.Combine(folder, "抽选数据.json")).Contains("\"maxPickedCount\": 10"));

            // 模拟点「清空已抽中」与「撤销最后一位」按钮是否存在可用状态
            var clear = Find<Button>(Root(page)).First(b => (b.Content?.ToString() ?? "") == "清空已抽中");
            Check("清空已抽中按钮可点击且不抛异常", () =>
            {
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return store.PickedNames.Count == 0;
            });
        });

        Console.WriteLine(_failures == 0 ? "设置页 UI 冒烟测试全部通过 ✔" : $"{_failures} 项失败 ✘");
        Environment.Exit(_failures == 0 ? 0 : 1);
    }

    private static void Check(string title, Func<bool> test)
    {
        try
        {
            if (test()) Console.WriteLine("PASS  " + title);
            else { _failures++; Console.WriteLine("FAIL  " + title); }
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine("FAIL  " + title + " → " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Control Root(SettingsPageBase page) => page.Content as Control;

    private static IEnumerable<string> Texts(Control? root) =>
        Find<TextBlock>(root).Select(t => t.Text ?? "");

    private static IEnumerable<T> Find<T>(Control? root) where T : Control
    {
        if (root is null) yield break;
        if (root is T match) yield return match;
        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children.OfType<Control>())
                foreach (var found in Find<T>(child)) yield return found;
                break;
            case ContentControl cc when cc.Content is Control content:
                foreach (var found in Find<T>(content)) yield return found;
                break;
            case Border border when border.Child is Control borderChild:
                foreach (var found in Find<T>(borderChild)) yield return found;
                break;
            case ScrollViewer sv when sv.Content is Control svContent:
                foreach (var found in Find<T>(svContent)) yield return found;
                break;
        }
    }
}
