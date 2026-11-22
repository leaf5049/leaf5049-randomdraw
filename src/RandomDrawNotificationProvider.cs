using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Attributes;

namespace Leaf5049.RandomDraw;

/// <summary>
/// 「随机抽选」的提醒提供方。抽中姓名后通过它显示 ClassIsland 原生通知遮罩。
/// </summary>
[NotificationProviderInfo(
    "EE288CC3-4BCA-45B5-944C-5CF024FADC2E",
    "随机抽选",
    "\ue02b",
    "随机抽选点名时显示的提醒遮罩。")]
public class RandomDrawNotificationProvider : NotificationProviderBase
{
}
