using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace Leaf5049.RandomDraw;

/// <summary>
/// 点名器的数据层：名单文件 + JSON 数据库 + 抽选规则。
/// </summary>
/// <remarks>
/// 数据结构（抽选数据.json）：
/// {
///   "allNames":    ["张三", "李四", ...],  // 读取的全部名单；为空时从 名单.txt 读取
///   "pickedNames": ["王五", ...]          // 已抽取的人名，按抽取先后顺序排列
/// }
/// </remarks>
public class DrawStore
{
    /// <summary>已抽中名单最多保留人数的默认值，超出后删除最早抽中的。</summary>
    public const int DefaultMaxPickedCount = 24;

    private readonly object _gate = new();
    private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
    private readonly string _folderPath;

    public string NamesFilePath { get; }
    public string DatabaseFilePath { get; }

    /// <summary>数据所在的插件文件夹。</summary>
    public string FolderPath => _folderPath;

    /// <summary>数据发生变化（名单或已抽中名单被修改）时触发，供设置界面刷新。</summary>
    public event EventHandler? Changed;

    public List<string> AllNames { get; private set; } = new();
    public List<string> PickedNames { get; private set; } = new();

    /// <summary>
    /// 滑窗大小：已抽中名单最多保留的人数，超出后删除最早抽中的。
    /// 可在插件设置页里修改，持久化在 抽选数据.json 的 maxPickedCount 字段。
    /// </summary>
    public int MaxPickedCount { get; private set; } = DefaultMaxPickedCount;

    public DrawStore(string folderPath)
    {
        _folderPath = folderPath;
        NamesFilePath = Path.Combine(folderPath, "名单.txt");
        DatabaseFilePath = Path.Combine(folderPath, "抽选数据.json");
        Load();
    }

    /// <summary>
    /// 读取数据：优先读 JSON 数据库；如果数据库里的全部名单为空，则从 名单.txt 读取并写回数据库。
    /// </summary>
    public void Load()
    {
        lock (_gate)
        {
            EnsureNamesFile();
            if (File.Exists(DatabaseFilePath))
            {
                try
                {
                    var json = File.ReadAllText(DatabaseFilePath, Encoding.UTF8);
                    var db = JsonSerializer.Deserialize<DatabaseModel>(json, JsonOptions);
                    if (db != null)
                    {
                        AllNames = Normalize(db.AllNames ?? new List<string>());
                        PickedNames = Normalize(db.PickedNames ?? new List<string>());
                        // 已抽中名单里可能存在不在全部名单中的过期记录，剔除掉。
                        PickedNames = PickedNames.Where(n => AllNames.Contains(n)).ToList();
                        MaxPickedCount = db.MaxPickedCount > 0 ? db.MaxPickedCount : DefaultMaxPickedCount;
                    }
                }
                catch (Exception)
                {
                    // 数据库损坏时不阻塞使用：退回用名单文件重建。
                }
            }

            if (AllNames.Count == 0)
            {
                AllNames = ReadNamesFromFile();
                if (AllNames.Count > 0)
                {
                    SaveInternal();
                }
            }
        }
    }

    /// <summary>
    /// 重新从 名单.txt 读取全部名单。
    /// </summary>
    public void ReloadNamesFromFile()
    {
        lock (_gate)
        {
            AllNames = ReadNamesFromFile();
            PickedNames = PickedNames.Where(n => AllNames.Contains(n)).ToList();
            SaveInternal();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 用界面里编辑的文本替换全部名单，并同步写回 名单.txt 与 JSON 数据库。
    /// 已抽中名单中不在新名单里的记录会被剔除。
    /// </summary>
    /// <returns>生效后的名单人数。</returns>
    public int SetNamesFromText(string? text)
    {
        lock (_gate)
        {
            var names = Normalize((text ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
            AllNames = names;
            PickedNames = PickedNames.Where(n => names.Contains(n)).ToList();
            SaveInternal();
            try
            {
                Directory.CreateDirectory(_folderPath);
                File.WriteAllText(NamesFilePath, string.Join(Environment.NewLine, names),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch (Exception)
            {
                // 写 txt 失败不影响 JSON 里的名单生效。
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return AllNames.Count;
        }
    }

    /// <summary>
    /// 设置滑窗大小（已抽中名单最多保留的人数）。超出当前人数的最早记录会被立即淘汰。
    /// </summary>
    /// <returns>生效后的滑窗大小。</returns>
    public int SetMaxPickedCount(int value)
    {
        lock (_gate)
        {
            MaxPickedCount = Math.Clamp(value, 1, 500);
            while (PickedNames.Count > MaxPickedCount)
            {
                PickedNames.RemoveAt(0);
            }
            SaveInternal();
            Changed?.Invoke(this, EventArgs.Empty);
            return MaxPickedCount;
        }
    }

    /// <summary>
    /// 手动清空已抽中名单。
    /// </summary>
    public void ClearPicked()
    {
        lock (_gate)
        {
            PickedNames.Clear();
            SaveInternal();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 撤销最后一次抽选，把最后抽中的人移出已抽中名单。
    /// </summary>
    /// <returns>被移出的人名；没有可撤销记录时返回 null。</returns>
    public string? RemoveLastPicked()
    {
        lock (_gate)
        {
            if (PickedNames.Count == 0)
            {
                return null;
            }
            var name = PickedNames[^1];
            PickedNames.RemoveAt(PickedNames.Count - 1);
            SaveInternal();
            Changed?.Invoke(this, EventArgs.Empty);
            return name;
        }
    }

    /// <summary>
    /// 手动清空已抽中名单（抽选逻辑本身不会调用它）。
    /// </summary>
    public void ResetPicked()
    {
        lock (_gate)
        {
            PickedNames.Clear();
            SaveInternal();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 从「已抽中人名」的补集中抽取一个人名，并记录到已抽中名单。
    /// 已抽中名单固定保留最近 <see cref="MaxPickedCount"/> 人，超出即淘汰最早抽中的，
    /// 因此抽中过的人要再等 24 次抽选才会重新回到候选池。
    /// 只有班级人数不超过 24 人、候选集被全部覆盖时，才会把最久没被抽到的人放回候选池继续轮转。
    /// </summary>
    /// <returns>抽中的人名。</returns>
    public string Draw()
    {
        lock (_gate)
        {
            if (AllNames.Count == 0)
            {
                AllNames = ReadNamesFromFile();
                if (AllNames.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"名单为空：请在插件文件夹的「名单.txt」中一行写一个姓名。文件位置：{NamesFilePath}");
                }
            }

            var candidates = GetCandidates();

            if (candidates.Count == 0)
            {
                // 只有班级人数 ≤ 冷却窗口（24）时才会走到这里：全班都在已抽中名单里。
                // 此时不清空记录（不清空就等于不清零公平性），而是把最久没被抽到的那个
                // 放回候选池，让抽选继续按"轮转"进行。
                PickedNames.RemoveAt(0);
                candidates = GetCandidates();
            }

            var name = candidates[NextIndex(candidates.Count)];
            PickedNames.Add(name);

            // 固定窗口：只保留最近 24 个抽中的人，超过就删除最早抽中的。
            while (PickedNames.Count > MaxPickedCount)
            {
                PickedNames.RemoveAt(0);
            }

            SaveInternal();
            Changed?.Invoke(this, EventArgs.Empty);
            return name;
        }
    }

    /// <summary>
    /// 候选集 = 全部名单 − 已抽中名单。
    /// </summary>
    private List<string> GetCandidates()
    {
        var picked = new HashSet<string>(PickedNames);
        return AllNames.Where(n => !picked.Contains(n)).ToList();
    }

    /// <summary>
    /// 保存数据到 JSON 数据库。
    /// </summary>
    public void Save()
    {
        lock (_gate)
        {
            SaveInternal();
        }
    }

    private void SaveInternal()
    {
        try
        {
            var model = new DatabaseModel
            {
                AllNames = AllNames,
                PickedNames = PickedNames,
                MaxPickedCount = MaxPickedCount
            };
            var json = JsonSerializer.Serialize(model, JsonOptions);
            var tempPath = DatabaseFilePath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(DatabaseFilePath))
            {
                File.Replace(tempPath, DatabaseFilePath, null);
            }
            else
            {
                File.Move(tempPath, DatabaseFilePath);
            }
        }
        catch (Exception)
        {
            // 保存失败（例如目录只读）不应让抽选整体崩溃，抽选结果仍在内存中。
        }
    }

    private void EnsureNamesFile()
    {
        if (File.Exists(NamesFilePath))
        {
            return;
        }
        Directory.CreateDirectory(_folderPath);
        var sample = new StringBuilder();
        sample.AppendLine("# 名单文件：一行写一个姓名，空行和以 # 开头的行会被忽略。");
        sample.AppendLine("# 第一次使用时请在下面把示例替换成你自己班级的名单。");
        sample.AppendLine("张三");
        sample.AppendLine("李四");
        sample.AppendLine("王五");
        File.WriteAllText(NamesFilePath, sample.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private List<string> ReadNamesFromFile()
    {
        if (!File.Exists(NamesFilePath))
        {
            return new List<string>();
        }
        var lines = File.ReadAllLines(NamesFilePath, Encoding.UTF8);
        return Normalize(lines);
    }

    private static List<string> Normalize(IEnumerable<string> source)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var raw in source)
        {
            if (raw == null)
            {
                continue;
            }
            var line = raw.Trim().Trim('\uFEFF');
            if (line.Length == 0 || line.StartsWith("#"))
            {
                continue;
            }
            if (seen.Add(line))
            {
                result.Add(line);
            }
        }
        return result;
    }

    private int NextIndex(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            return 0;
        }
        // 使用操作系统的熵源，避免“同一毫秒连续抽选抽到同一个人”之类的伪随机问题。
        var bytes = new byte[4];
        _rng.GetBytes(bytes);
        var value = BitConverter.ToUInt32(bytes, 0);
        return (int)(value % (uint)exclusiveMax);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public class DatabaseModel
    {
        [JsonPropertyName("allNames")]
        public List<string> AllNames { get; set; } = new();

        [JsonPropertyName("pickedNames")]
        public List<string> PickedNames { get; set; } = new();

        [JsonPropertyName("maxPickedCount")]
        public int MaxPickedCount { get; set; } = DefaultMaxPickedCount;
    }
}
