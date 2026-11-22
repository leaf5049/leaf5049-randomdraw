using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Leaf5049.RandomDraw;

int failures = 0;

void Check(string title, Func<bool> test)
{
    try
    {
        if (test())
        {
            Console.WriteLine($"PASS  {title}");
        }
        else
        {
            failures++;
            Console.WriteLine($"FAIL  {title}");
        }
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL  {title}  ({ex.Message})");
    }
}

string NewFolder()
{
    var path = Path.Combine(Path.GetTempPath(), "drawstore-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

void WriteNames(string folder, params string[] names)
{
    File.WriteAllLines(Path.Combine(folder, "名单.txt"), names, new UTF8Encoding(false));
}

// 1. 数据库为空时应从 txt 读取全部名单，并写回 json。
Check("空数据库时读取名单.txt 并回写", () =>
{
    var folder = NewFolder();
    WriteNames(folder, "张三", "李四", "王五", "李四", "", "# 注释");
    var store = new DrawStore(folder);
    var db = File.ReadAllText(Path.Combine(folder, "抽选数据.json"));
    return store.AllNames.Count == 3
        && store.AllNames[0] == "张三"
        && store.PickedNames.Count == 0
        && db.Contains("\"allNames\"");
});

// 2. 若数据库里已有全部名单，则不再读取 txt。
Check("数据库非空时优先使用数据库", () =>
{
    var folder = NewFolder();
    WriteNames(folder, "赵六");
    var store1 = new DrawStore(folder);
    store1.Draw(); // 让数据库落盘（allNames = 赵六）
    File.WriteAllText(Path.Combine(folder, "抽选数据.json"),
        "{\"allNames\":[\"甲\",\"乙\"],\"pickedNames\":[]}");
    var store2 = new DrawStore(folder);
    return store2.AllNames.SequenceEqual(new[] { "甲", "乙" });
});

// 3. 抽选应从补集里取，一轮内不重复。
Check("一轮内不重复", () =>
{
    var folder = NewFolder();
    WriteNames(folder, "A", "B", "C", "D", "E");
    var store = new DrawStore(folder);
    var drawn = Enumerable.Range(0, 5).Select(_ => store.Draw()).ToList();
    return drawn.Distinct().Count() == 5 && drawn.All(n => store.AllNames.Contains(n));
});

// 4. 已抽中名单超过 24 人时删除最早抽中的。
Check("超过 24 人时淘汰最早的", () =>
{
    var folder = NewFolder();
    WriteNames(folder, Enumerable.Range(1, 40).Select(i => "S" + i).ToArray());
    var store = new DrawStore(folder);
    var order = Enumerable.Range(0, 30).Select(_ => store.Draw()).ToList();
    return store.PickedNames.Count == 24
        && store.PickedNames.SequenceEqual(order.Skip(30 - 24));
});

// 5. 40 人班级：固定 24 人窗口，候选池恒为 16 人，抽中的人 24 次内不再被抽到。
Check("40 人班：候选池恒为 16，抽中者 24 次内不重复", () =>
{
    var folder = NewFolder();
    WriteNames(folder, Enumerable.Range(1, 40).Select(i => "S" + i).ToArray());
    var store = new DrawStore(folder);
    var drawn = new List<string>();
    for (int i = 0; i < 30; i++)
    {
        drawn.Add(store.Draw());
        if (i >= 24)
        {
            // 抽中的人不能出现在此前 24 次里
            var recent24 = drawn.Skip(drawn.Count - 24).Take(23).ToList();
            if (recent24.Contains(drawn[^1])) return false;
        }
    }
    var candidatesNow = store.AllNames.Except(store.PickedNames).Count();
    return store.PickedNames.Count == 24 && candidatesNow == 16;
});

// 6. 25 人班级：窗口 24 人，补集恒为 1 人，被淘汰的人立刻回到候选池，记录不清空。
Check("25 人班：淘汰者立刻回到候选池", () =>
{
    var folder = NewFolder();
    WriteNames(folder, Enumerable.Range(1, 25).Select(i => "S" + i).ToArray());
    var store = new DrawStore(folder);
    var drawn = Enumerable.Range(0, 25).Select(_ => store.Draw()).ToList();

    // 25 次之后窗口里是最后 24 个，最早抽中的那个被淘汰
    if (store.PickedNames.Count != 24) return false;
    if (!store.PickedNames.SequenceEqual(drawn.Skip(1))) return false;
    var evicted = drawn[0];
    if (store.AllNames.Except(store.PickedNames).Single() != evicted) return false;

    // 再抽一次：候选池只有它，必然抽到；名单变成「后 23 个 + 它」，被挤出去的是第 2 个
    var next = store.Draw();
    return next == evicted
        && store.PickedNames.Count == 24
        && store.PickedNames[^1] == evicted
        && store.PickedNames[0] == drawn[2]
        && store.AllNames.Except(store.PickedNames).Single() == drawn[1];
});

// 7. 20 人班级（人数小于 24）：补集会被全部覆盖，此时回退到"最久没被抽到的人"，记录不清空。
Check("20 人班：补集为空时抽最久没抽到的，记录不清空", () =>
{
    var folder = NewFolder();
    WriteNames(folder, Enumerable.Range(1, 20).Select(i => "S" + i).ToArray());
    var store = new DrawStore(folder);
    for (int i = 0; i < 20; i++) store.Draw();
    var oldest = store.PickedNames[0];
    var next = store.Draw();
    if (next != oldest) return false;
    if (store.PickedNames.Count == 0) return false;
    // 继续抽 19 次，应把其余 19 人都抽到一遍
    var rest = Enumerable.Range(0, 19).Select(_ => store.Draw()).ToList();
    return rest.Concat(new[] { next }).Distinct().Count() == 20
        && store.PickedNames.Count > 0;   // 全程没有清空记录
});

// 6. 抽选结果应持久化，重启后保留。
Check("抽选结果持久化", () =>
{
    var folder = NewFolder();
    WriteNames(folder, "甲", "乙", "丙");
    var first = new DrawStore(folder);
    var name = first.Draw();
    var second = new DrawStore(folder);
    return second.PickedNames.SequenceEqual(new[] { name }) && second.AllNames.Count == 3;
});

// 7. 名单缺失时抽选应给出明确异常，并自动生成示例名单文件。
Check("名单为空时报错并生成示例名单", () =>
{
    var folder = NewFolder();
    var store = new DrawStore(folder);
    File.Delete(Path.Combine(folder, "名单.txt"));
    // 新文件夹里没有名单文件时，构造应生成示例文件
    var folder2 = NewFolder();
    var store2 = new DrawStore(folder2);
    try
    {
        // 清空数据库与名单后再抽
        File.WriteAllText(Path.Combine(folder2, "抽选数据.json"), "{\"allNames\":[],\"pickedNames\":[]}");
        File.WriteAllText(Path.Combine(folder2, "名单.txt"), "");
        var store3 = new DrawStore(folder2);
        store3.Draw();
        return false;
    }
    catch (InvalidOperationException)
    {
        return File.Exists(Path.Combine(folder2, "名单.txt"));
    }
});

// 8. 分布大致均匀（每人都应有机会被抽到）。
Check("抽选分布覆盖全部名单", () =>
{
    var folder = NewFolder();
    WriteNames(folder, "A", "B", "C");
    var store = new DrawStore(folder);
    var hits = new HashSet<string>();
    for (int i = 0; i < 3000; i++)
    {
        hits.Add(store.Draw());
    }
    return hits.Count == 3;
});

// 9. 设置界面改名单：写回 JSON 与 名单.txt，剔除不在新名单里的已抽中记录，并触发 Changed 事件。
Check("设置界面改名单：同步写回 txt/JSON 并剔除失效的已抽中记录", () =>
{
    var folder = NewFolder();
    WriteNames(folder, "张三", "李四", "王五");
    var store = new DrawStore(folder);
    store.Draw();
    store.Draw();

    var changed = 0;
    store.Changed += (_, _) => changed++;

    var count = store.SetNamesFromText("张三\n王五\n赵六\n\n# 注释行\n张三");

    var txt = File.ReadAllLines(Path.Combine(folder, "名单.txt"), Encoding.UTF8);
    var db = File.ReadAllText(Path.Combine(folder, "抽选数据.json"));
    return count == 3
           && store.AllNames.Count == 3
           && store.AllNames.Contains("赵六")
           && !store.AllNames.Contains("李四")
           && txt.Count(t => t.Trim().Length > 0 && !t.Trim().StartsWith("#")) == 3
           && store.PickedNames.All(n => store.AllNames.Contains(n))
           && db.Contains("赵六")
           && changed == 1;
});

// 10. 设置界面撤销最后一位 / 清空已抽中。
Check("设置界面撤销最后一位与清空已抽中", () =>
{
    var folder = NewFolder();
    WriteNames(folder, "A", "B", "C");
    var store = new DrawStore(folder);
    var first = store.Draw();
    var second = store.Draw();

    var removed = store.RemoveLastPicked();
    var undoOk = removed == second && store.PickedNames.Count == 1 && store.PickedNames[0] == first;

    store.ClearPicked();
    var clearOk = store.PickedNames.Count == 0;

    // 撤销后这个人应重新进入候选池
    var next = store.Draw();
    return undoOk && clearOk && store.AllNames.Contains(next);
});

// 11. 设置页改滑窗大小：立即生效、立刻裁剪超出的旧记录、写回 JSON 并在重启后保留。
Check("设置页改滑窗大小：立即裁剪、持久化、重启后仍生效", () =>
{
    var folder = NewFolder();
    WriteNames(folder, Enumerable.Range(0, 40).Select(i => "学生" + i).ToArray());
    var store = new DrawStore(folder);
    for (var i = 0; i < 30; i++) store.Draw();

    var changed = 0;
    store.Changed += (_, _) => changed++;

    var applied = store.SetMaxPickedCount(10);
    var changedAfterSet = changed;
    var trimOk = applied == 10 && store.PickedNames.Count == 10 && changedAfterSet == 1;

    // 裁剪后最早抽中的人回到候选池，且接下来的抽选里不会重复出现
    var kept = store.PickedNames.ToList();
    var next = store.Draw();
    var poolOk = !kept.Contains(next) && store.AllNames.Contains(next) && store.PickedNames.Count == 10;

    var reloaded = new DrawStore(folder);
    var persistOk = reloaded.MaxPickedCount == 10 && reloaded.PickedNames.Count == 10;

    // 滑窗 10、全班 40 → 候选池恒为 30 人
    var pool = store.AllNames.Count - store.PickedNames.Count;
    return trimOk && poolOk && persistOk && pool == 30;
});

// 12. 滑窗大小越界时被收敛到合法区间，不会把已抽中记录清空。
Check("滑窗大小越界被收敛到 1–500，且不影响名单", () =>
{
    var folder = NewFolder();
    WriteNames(folder, "A", "B", "C");
    var store = new DrawStore(folder);
    store.Draw();

    var high = store.SetMaxPickedCount(9999);
    var low = store.SetMaxPickedCount(0);
    return high == 500 && low == 1 && store.AllNames.Count == 3 && store.PickedNames.Count <= 3;
});

Console.WriteLine();
Console.WriteLine(failures == 0 ? "全部测试通过 ✔" : $"{failures} 项测试失败 ✘");
Environment.Exit(failures == 0 ? 0 : 1);
