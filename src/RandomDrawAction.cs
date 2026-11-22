using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Notification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Leaf5049.RandomDraw;

/// <summary>
/// 「随机抽选」行动：从全名单中、排除已抽中的人，随机抽一个人，
/// 用 ClassIsland 原生通知遮罩显示 3 秒并朗读，然后记录到已抽中名单。
/// </summary>
[ActionInfo("leaf5049.randomdraw.pick", "随机抽选", "\ue02b", true, "点名")]
public class RandomDrawAction : ActionBase
{
    /// <summary>遮罩显示时长。</summary>
    private static readonly TimeSpan MaskDuration = TimeSpan.FromSeconds(3);

    private readonly DrawStore _store;
    private readonly ILogger<RandomDrawAction> _logger;

    /// <summary>
    /// 测试注入点：设置后提醒请求改交给它处理，不再经由提醒提供方。
    /// 仅用于离线测试，生产环境保持为 null。
    /// </summary>
    internal static Func<ClassIsland.Core.Models.Notification.NotificationRequest, Task>? NotificationSink;

    public RandomDrawAction(DrawStore store, ILogger<RandomDrawAction> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        string name;
        try
        {
            name = _store.Draw();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "随机抽选失败");
            await ShowMaskAsync("点名失败：名单为空", TimeSpan.FromSeconds(5));
            return;
        }

        _logger.LogInformation("随机抽选中：{Name}（已抽中 {Count} 人）", name, _store.PickedNames.Count);
        await ShowMaskAsync(name, MaskDuration);
    }

    private async Task ShowMaskAsync(string text, TimeSpan duration)
    {
        var request = new ClassIsland.Core.Models.Notification.NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(
                text,
                "lucide(\ue0ff)",
                "lucide(\ue224)",
                hasRightIcon: false,
                factory: content =>
                {
                    content.Duration = duration;
                    content.IsSpeechEnabled = true;
                }),
            RequestNotificationSettings = new ClassIsland.Shared.Models.Notification.NotificationSettings
            {
                IsSettingsEnabled = true,
                IsNotificationEnabled = true,
                IsSpeechEnabled = true,
                IsNotificationEffectEnabled = true,
                IsNotificationSoundEnabled = false,
                IsNotificationTopmostEnabled = true
            }
        };

        try
        {
            if (NotificationSink != null)
            {
                await NotificationSink(request);
                return;
            }

            var provider = FindProvider();
            if (provider == null)
            {
                _logger.LogWarning("随机抽选：找不到提醒提供方，遮罩未显示");
                return;
            }

            // 提醒必须在 UI 线程上发起；这里只需要“把提醒推出去”，
            // 不等它播放完（ShowNotificationAsync 会一直等到遮罩结束才返回）。
            await Dispatcher.UIThread.InvokeAsync(() => provider.ShowNotification(request));

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示随机抽选遮罩时出错");
        }
    }

    /// <summary>
    /// 取自己注册的提醒提供方实例。ClassIsland 通过 AddHostedService 注册提供方，
    /// 实例在提醒宿主里注册过渠道，必须取这一个实例，不能再 new 一个新的。
    /// </summary>
    private static RandomDrawNotificationProvider? FindProvider()
    {
        return IAppHost.Host?.Services.GetServices<IHostedService>()
            .OfType<RandomDrawNotificationProvider>()
            .FirstOrDefault();
    }
}
