using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services.NotificationProviders;
using ClassIsland.Core.Models.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 模拟 ClassIsland 加载插件的流程：反射加载入口程序集 -> 找到 PluginBase -> 调用 Initialize ->
// 检查服务注册是否齐全（行动、提醒提供方、数据层、提醒发送方）。
var appDir = Environment.GetEnvironmentVariable("CLASSISLAND_DIR");
if (string.IsNullOrWhiteSpace(appDir) || !Directory.Exists(appDir))
{
    Console.WriteLine("请先设置环境变量 CLASSISLAND_DIR，指向 ClassIsland 安装目录（目录里应有 ClassIsland.Core.dll）。");
    Environment.Exit(1);
}
var pluginDir = Path.Combine(Path.GetTempPath(), "plugin-reg-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(pluginDir);

var alc = new PluginLoadContext(dir => Path.Combine(appDir, dir));
var asm = alc.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "Leaf5049.RandomDraw.dll"));
var pluginType = asm.GetExportedTypes().FirstOrDefault(t => typeof(PluginBase).IsAssignableFrom(t));
if (pluginType == null)
{
    Console.WriteLine("FAIL  找不到插件入口类（PluginBase 子类）");
    Environment.Exit(1);
}

var plugin = (PluginBase)Activator.CreateInstance(pluginType)!;
var info = new PluginInfo
{
    Manifest = new PluginManifest { Id = "leaf5049.randomdraw", Name = "随机抽选" }
};
typeof(PluginInfo).GetProperty("PluginFolderPath")!.SetValue(info, pluginDir);
var infoProp = typeof(PluginBase).GetProperty("Info")!;
infoProp.SetValue(plugin, info);

var services = new ServiceCollection();
plugin.Initialize(null!, services);

var descriptors = services.ToList();
int failures = 0;
void Check(string title, Func<bool> test)
{
    if (test()) Console.WriteLine("PASS  " + title);
    else { failures++; Console.WriteLine("FAIL  " + title); }
}

Check("入口类实现 PluginBase", () => pluginType.Name == "Plugin" && plugin is not null);
Check("行动本身不再带设置界面（改回 AddAction<T> 单参数注册）", () =>
    !descriptors.Any(d => d.ServiceType == typeof(ClassIsland.Core.Abstractions.Controls.ActionSettingsControlBase)));
Check("注册了插件专用设置页（keyed SettingsPageBase，id leaf5049.randomdraw.settings）", () =>
    descriptors.Any(d => d.ServiceType == typeof(ClassIsland.Core.Abstractions.Controls.SettingsPageBase)
                         && d.IsKeyedService
                         && (string?)d.ServiceKey == "leaf5049.randomdraw.settings"
                         && d.KeyedImplementationType?.Name == "RandomDrawSettingsPage"));
Check("设置页进入全局设置页注册表（设置窗口里能看到）", () =>
    ClassIsland.Core.Services.Registry.SettingsWindowRegistryService.Registered
        .Any(p => p.Id == "leaf5049.randomdraw.settings"));

Check("注册了「随机抽选」行动（keyed ActionBase）", () => descriptors.Any(d =>
    d.ServiceType == typeof(ActionBase)
    && d.ServiceKey as string == "leaf5049.randomdraw.pick"
    && d.Lifetime == ServiceLifetime.Transient));
Check("注册了提醒提供方（hosted service）", () => descriptors.Any(d =>
    d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "RandomDrawNotificationProvider"));
Check("提醒提供方是行动要用的同一个 hosted 实例（不再单独注册 INotificationSender）", () =>
    descriptors.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "RandomDrawNotificationProvider")
    && !descriptors.Any(d => d.ServiceType == typeof(INotificationSender)));
Check("注册了数据层 DrawStore 单例", () => descriptors.Any(d =>
    d.ServiceType?.Name == "DrawStore" && d.Lifetime == ServiceLifetime.Singleton));
Check("提醒提供方注册进全局注册表", () =>
    ClassIsland.Core.Services.Registry.NotificationProviderRegistryService.RegisteredProviders
        .Any(p => p.Guid.ToString().Equals("EE288CC3-4BCA-45B5-944C-5CF024FADC2E", StringComparison.OrdinalIgnoreCase)));
Check("行动出现在全局行动信息表与「点名」菜单组", () =>
{
    var hasInfo = ClassIsland.Core.Abstractions.Services.IActionService.ActionInfos.ContainsKey("leaf5049.randomdraw.pick");
    var hasMenu = ClassIsland.Core.Abstractions.Services.IActionService.ActionMenuTree
        .Any(g => g.Name == "点名" && ((ClassIsland.Core.Models.Automation.ActionMenuTreeGroup)g).Children
            .OfType<ClassIsland.Core.Models.Automation.ActionMenuTreeItem>()
            .Any(c => c.ActionItemId == "leaf5049.randomdraw.pick"));
    return hasInfo && hasMenu;
});

Console.WriteLine(failures == 0 ? "插件注册流程全部通过 ✔" : $"{failures} 项失败 ✘");
Environment.Exit(failures == 0 ? 0 : 1);

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly Func<string, string> _resolver;
    public PluginLoadContext(Func<string, string> resolver) : base(true) => _resolver = resolver;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // 和 ClassIsland 一样：插件程序集自己装载，其余（Core/Shared/Avalonia 等）用宿主已加载的版本。
        if (assemblyName.Name == "Leaf5049.RandomDraw")
        {
            var path = _resolver(assemblyName.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
        return null;
    }
}
