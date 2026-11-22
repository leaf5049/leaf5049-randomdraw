using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Core.Models.Notification.Templates;
using ClassIsland.Shared.Models.Notification;
using Microsoft.Extensions.Logging.Abstractions;
using Leaf5049.RandomDraw;

// 这个 harness 不启动 ClassIsland，直接调用行动的逻辑，检查它构造的提醒请求是否符合预期。
var folder = Path.Combine(Path.GetTempPath(), "drawstore-harness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
File.WriteAllLines(Path.Combine(folder, "名单.txt"), new[] { "张三", "李四", "王五" }, new UTF8Encoding(false));

var store = new DrawStore(folder);
var requests = new System.Collections.Generic.List<ClassIsland.Core.Models.Notification.NotificationRequest>();
var sinkField = typeof(RandomDrawAction).GetField(
    "NotificationSink",
    BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("找不到 NotificationSink 测试注入点");
sinkField.SetValue(null, new Func<ClassIsland.Core.Models.Notification.NotificationRequest, Task>(r =>
{
    requests.Add(r);
    return Task.CompletedTask;
}));
var action = new RandomDrawAction(store, NullLogger<RandomDrawAction>.Instance);

var onInvoke = typeof(ActionBase).GetMethod("OnInvoke", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("找不到 OnInvoke");
await (Task)onInvoke.Invoke(action, null)!;

Console.WriteLine($"捕获到提醒请求：{requests.Count} 个");
var failures = 0;
void Check(string title, Func<bool> test)
{
    if (test()) Console.WriteLine("PASS  " + title);
    else { failures++; Console.WriteLine("FAIL  " + title); }
}

if (requests.Count != 1)
{
    Console.WriteLine("FAIL  应该只发出 1 个提醒");
    Environment.Exit(1);
}

var request = requests[0];
var mask = request.MaskContent;
var templateData = mask?.Content as TwoIconsMaskTemplateData;

Check("使用通知遮罩（TwoIconsMask 模板）", () => mask != null && mask.ContentTemplateResourceKey as string == "NotificationTwoIconsMaskTemplate");
Check("遮罩文本是抽到的姓名", () => templateData != null && store.PickedNames.Count == 1 && templateData.Text == store.PickedNames[0]);
Check("遮罩时长 3 秒", () => mask!.Duration.TotalSeconds == 3.0);
Check("启用朗读且朗读内容为姓名", () => mask!.IsSpeechEnabled && mask.SpeechContent == templateData!.Text);
Check("关掉提醒音效、保留语音/特效/置顶", () =>
{
    var s = request.RequestNotificationSettings;
    return s != null && s.IsSettingsEnabled && s.IsSpeechEnabled && !s.IsNotificationSoundEnabled
           && s.IsNotificationEffectEnabled && s.IsNotificationTopmostEnabled;
});
Check("抽中的人写入已抽中名单", () => store.PickedNames.Count == 1 && store.AllNames.Contains(store.PickedNames[0]));

// 再抽一次：不应和上一次重复
await (Task)onInvoke.Invoke(action, null)!;
Check("连续两次抽选不重复", () => store.PickedNames.Count == 2 && store.PickedNames.Distinct().Count() == 2);

Console.WriteLine(failures == 0 ? "行动逻辑测试全部通过 ✔" : $"{failures} 项失败 ✘");
Environment.Exit(failures == 0 ? 0 : 1);
