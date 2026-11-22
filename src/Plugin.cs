using System;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Leaf5049.RandomDraw;

/// <summary>
/// 插件入口。注册点名器的数据层、提醒提供方和「随机抽选」行动。
/// </summary>
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 名单.txt 与 抽选数据.json 放在插件自己的文件夹里。
        var dataFolder = string.IsNullOrWhiteSpace(Info?.PluginFolderPath)
            ? AppContext.BaseDirectory
            : Info.PluginFolderPath;

        services.AddSingleton(new DrawStore(dataFolder));

        // 提醒提供方以 IHostedService 形式注册（AddNotificationProvider 内部就是 AddHostedService），
        // 行动运行时从 IHostedService 里取这同一个实例来发提醒，
        // 不要额外注册 INotificationSender 工厂去 new 第二个实例。
        services.AddNotificationProvider<RandomDrawNotificationProvider>();

        // 行动本身不带设置面板，改在插件的专用设置页里统一管理（设置 → 随机抽选）。
        services.AddAction<RandomDrawAction>();
        services.AddSettingsPage<RandomDrawSettingsPage>();
    }
}
