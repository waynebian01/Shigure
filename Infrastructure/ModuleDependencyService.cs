using System.Text;
namespace Shigure;

internal sealed class ModuleDependencyService
{
    private static readonly string[] StateCategories =
    [
        ClassStateCatalog.CategoryState,
        ClassStateCatalog.CategorySpecial,
        ClassStateCatalog.CategoryResource,
        ClassStateCatalog.CategoryConfig,
        ClassStateCatalog.CategoryTarget,
        ClassStateCatalog.CategoryFocus,
        ClassStateCatalog.CategoryMouseover,
        ClassStateCatalog.CategoryPet,
        ClassStateCatalog.CategoryBoss1,
        ClassStateCatalog.CategoryBoss2,
        ClassStateCatalog.CategoryBoss3,
        ClassStateCatalog.CategoryBoss4,
        ClassStateCatalog.CategoryBoss5
    ];

    // “锚点”是配置协议使用但不在可编辑字段目录中展示的固定字段。
    private static readonly HashSet<string> HiddenFixedStateFields = new(StringComparer.Ordinal)
    {
        "锚点"
    };

    private readonly string _classDirectory;
    private readonly string _classMacrosPath;
    private readonly object _gate = new();

    public ModuleDependencyService(string baseDirectory)
    {
        var addonRoot = Path.Combine(baseDirectory, "Fuyutsui");
        _classDirectory = Path.Combine(addonRoot, "class");
        _classMacrosPath = Path.Combine(addonRoot, "core", "classmacros.lua");
    }

    public string? Capture(ModuleDefinition module)
    {
        lock (_gate)
        {
            return CaptureCore(module);
        }
    }

    private string? CaptureCore(ModuleDefinition module)
    {
        var classId = module.Match.ClassId;
        var specId = module.Match.SpecId;
        if (classId is null || specId is null)
        {
            module.Dependencies = null;
            return "模块未同时指定职业和专精，已保存模块逻辑，但未携带配置和宏。";
        }

        var classPath = ResolveClassPath(classId.Value);
        var configDocument = ClassBlocksStore.Load(classPath);
        if (!configDocument.IsModernFormat)
        {
            throw new InvalidOperationException($"{Path.GetFileName(classPath)} 仍是旧版配置格式，无法随模块保存。");
        }

        if (!configDocument.Specs.TryGetValue(specId.Value, out var spec))
        {
            throw new InvalidOperationException($"职业 {classId} 中不存在专精 {specId} 的配置。");
        }

        var macrosDocument = ClassMacrosStore.Load(_classMacrosPath);
        var classKey = ClassMacrosStore.ToClassFileKey(classId.Value);
        if (!macrosDocument.Classes.TryGetValue(classKey, out var macros))
        {
            throw new InvalidOperationException($"classmacros.lua 中不存在职业 {classKey} 的宏配置。");
        }

        EnsureMacroCapacity(classId.Value, macros);
        module.Dependencies = new ModuleDependencySnapshot
        {
            ClassId = classId.Value,
            SpecId = specId.Value,
            Config = new ModuleConfigSnapshot
            {
                Spec = CaptureSpec(spec),
                SpellsList = configDocument.SpellsList.Select(entry => new ModuleSpellListEntrySnapshot
                {
                    SpellId = entry.SpellId,
                    Index = entry.Index,
                    Name = entry.Name
                }).ToList(),
                ItemsList = configDocument.ItemsList.Select(entry => new ModuleItemListEntrySnapshot
                {
                    ItemId = entry.ItemId,
                    Index = entry.Index,
                    Name = entry.Name
                }).ToList()
            },
            Macros = new ModuleMacrosSnapshot
            {
                DynamicCommon = new List<string>(macros.DynamicSpells),
                // 旧字段保留为空，仅用于兼容读取历史模块文件。
                UsesSpecDynamicSpells = false,
                DynamicForSpec = [],
                StaticSpells = CompactMacroSnapshots(macros.StaticSpells.Select(CaptureMacro), isSpecial: false),
                SpecialSpells = CompactMacroSnapshots(macros.SpecialSpells.Select(CaptureMacro), isSpecial: true)
            }
        };
        return null;
    }

    public ModuleDependencyImportResult Import(IReadOnlyList<ModuleDefinition> modules)
    {
        lock (_gate)
        {
            return ImportCore(modules);
        }
    }

    private ModuleDependencyImportResult ImportCore(IReadOnlyList<ModuleDefinition> modules)
    {
        var result = new ModuleDependencyImportResult();
        foreach (var module in modules
                     .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            if (module.Dependencies is null)
            {
                continue;
            }

            try
            {
                ValidateSnapshot(module, module.Dependencies);
                var removedFields = RemoveUnknownStateFields(module.Dependencies.Config.Spec);
                if (removedFields.Count > 0)
                {
                    result.SanitizedModules.Add(module.Clone());
                    result.RemovedStateFields.AddRange(removedFields.Select(field =>
                        new RemovedModuleStateField(module.Name, field.Category, field.Name)));
                }

                ImportOne(module, result);
            }
            catch (Exception ex)
            {
                result.Rejected.Add(new RejectedModuleDependency(module.Id, module.Name, ex.Message));
            }
        }

        return result;
    }

    private void ImportOne(ModuleDefinition module, ModuleDependencyImportResult result)
    {
        var snapshot = module.Dependencies!;

        var classPath = ResolveClassPath(snapshot.ClassId);
        var configDocument = ClassBlocksStore.Load(classPath);
        if (!configDocument.IsModernFormat)
        {
            throw new InvalidOperationException($"{Path.GetFileName(classPath)} 仍是旧版配置格式。");
        }

        if (!configDocument.Specs.TryGetValue(snapshot.SpecId, out var localSpec))
        {
            localSpec = new ClassBlocksStore.SpecBlocks();
            configDocument.Specs[snapshot.SpecId] = localSpec;
        }

        var macrosDocument = ClassMacrosStore.Load(_classMacrosPath);
        var classKey = ClassMacrosStore.ToClassFileKey(snapshot.ClassId);
        if (!macrosDocument.Classes.TryGetValue(classKey, out var localMacros))
        {
            throw new InvalidOperationException($"classmacros.lua 中不存在职业 {classKey} 的宏配置。");
        }

        var counters = new MergeCounters();
        MergeSpec(localSpec, snapshot.Config.Spec, counters);
        MergeSpellsList(configDocument.SpellsList, snapshot.Config.SpellsList, counters);
        MergeItemsList(configDocument.ItemsList, snapshot.Config.ItemsList, counters);
        MergeMacros(localMacros, snapshot.ClassId, snapshot.Macros, counters);
        EnsureMacroCapacity(snapshot.ClassId, localMacros);

        if (!counters.HasConfigChanges && counters.MacrosAdded == 0)
        {
            result.Conflicts.AddRange(counters.Conflicts.Select(message => $"{module.Name}: {message}"));
            if (counters.Conflicts.Count > 0)
            {
                result.ConflictedModuleIds.Add(module.Id);
            }
            return;
        }

        CommitDocuments(configDocument, macrosDocument, counters.HasConfigChanges, counters.MacrosAdded > 0);
        result.ConfigAdded += counters.ConfigAdded;
        result.ConfigUpdated += counters.ConfigUpdated;
        result.MacrosAdded += counters.MacrosAdded;
        result.ChangedModules.Add(module.Name);
        result.Conflicts.AddRange(counters.Conflicts.Select(message => $"{module.Name}: {message}"));
        if (counters.Conflicts.Count > 0)
        {
            result.ConflictedModuleIds.Add(module.Id);
        }
    }

    private static void ValidateSnapshot(ModuleDefinition module, ModuleDependencySnapshot snapshot)
    {
        if (snapshot.SchemaVersion is < 1 or > ModuleDependencySnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持依赖快照版本 {snapshot.SchemaVersion}。");
        }

        UpgradeLegacyItems(snapshot);

        if (module.Match.ClassId != snapshot.ClassId || module.Match.SpecId != snapshot.SpecId)
        {
            throw new InvalidDataException("依赖快照的职业/专精与模块匹配条件不一致。");
        }

        var unknownCategory = snapshot.Config.Spec.CategorizedStates.Keys
            .FirstOrDefault(key => !StateCategories.Contains(key, StringComparer.Ordinal));
        if (unknownCategory is not null)
        {
            throw new InvalidDataException($"依赖快照包含未知状态分类“{unknownCategory}”。");
        }

        var auraGroups = new[]
        {
            snapshot.Config.Spec.PlayerAuras,
            snapshot.Config.Spec.TargetHarmfulAuras,
            snapshot.Config.Spec.TargetHelpfulAuras,
            snapshot.Config.Spec.FocusHarmfulAuras,
            snapshot.Config.Spec.FocusHelpfulAuras
        };
        if (auraGroups.SelectMany(entries => entries ?? [])
            .Any(aura => !GetAuraSpellIds(aura.SpellId, aura.SpellIds).Any()))
        {
            throw new InvalidDataException("依赖快照包含缺少有效 spellId 的光环。");
        }

        if ((snapshot.Config.Spec.Spells ?? []).Any(spell => !IsValidSpellId(spell.SpellId)))
        {
            throw new InvalidDataException("依赖快照包含缺少有效 spellId 的法术。");
        }

        var items = snapshot.Config.Spec.Items ?? [];
        var itemReservedNames = new HashSet<string>(StringComparer.Ordinal);
        if (snapshot.Config.Spec.NestedStates)
        {
            foreach (var category in new[]
                     {
                         ClassStateCatalog.CategoryState,
                         ClassStateCatalog.CategorySpecial,
                         ClassStateCatalog.CategoryResource,
                         ClassStateCatalog.CategoryConfig
                     })
            {
                itemReservedNames.UnionWith(
                    snapshot.Config.Spec.CategorizedStates.GetValueOrDefault(category) ?? []);
            }
        }
        else
        {
            itemReservedNames.UnionWith(snapshot.Config.Spec.FlatStates ?? []);
        }
        if (items.Any(item => item.ItemId <= 0 || string.IsNullOrWhiteSpace(item.Name))
            || items.Select(item => item.ItemId).Distinct().Count() != items.Count
            || items.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != items.Count
            || items.Any(item => itemReservedNames.Contains(item.Name)))
        {
            throw new InvalidDataException("依赖快照包含无效或重复的物品 itemId/name。");
        }

        if ((snapshot.Config.SpellsList ?? []).Any(spell => !IsValidSpellId(spell.SpellId)))
        {
            throw new InvalidDataException("依赖快照包含缺少有效 spellId 的技能列表条目。");
        }

        if ((snapshot.Config.ItemsList ?? []).Any(item => !IsValidItemId(item.ItemId)))
        {
            throw new InvalidDataException("依赖快照包含缺少有效 itemId 的物品列表条目。");
        }
    }

    private static void UpgradeLegacyItems(ModuleDependencySnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1)
        {
            return;
        }

        var spec = snapshot.Config.Spec;
        spec.Items ??= [];
        if (spec.CategorizedStates.TryGetValue(ClassStateCatalog.CategoryItem, out var legacyNames))
        {
            foreach (var name in legacyNames.ToArray())
            {
                if (ClassStateCatalog.TryGetLegacyItemId(name, out var itemId)
                    && spec.Items.All(item => item.ItemId != itemId))
                {
                    spec.Items.Add(new ModuleItemSnapshot { ItemId = itemId, Name = name, IsEquipped = false });
                    legacyNames.Remove(name);
                }
            }

            spec.CategorizedStates.Remove(ClassStateCatalog.CategoryItem);
        }

        snapshot.SchemaVersion = ModuleDependencySnapshot.CurrentSchemaVersion;
    }

    private static List<RemovedStateField> RemoveUnknownStateFields(ModuleSpecSnapshot spec)
    {
        var removed = new List<RemovedStateField>();
        if (spec.NestedStates)
        {
            foreach (var (category, fields) in spec.CategorizedStates)
            {
                if (fields is null)
                {
                    continue;
                }

                for (var index = fields.Count - 1; index >= 0; index--)
                {
                    var field = fields[index]?.Trim() ?? string.Empty;
                    if (IsRecognizedStateField(category, field))
                    {
                        continue;
                    }

                    fields.RemoveAt(index);
                    if (field.Length > 0)
                    {
                        removed.Add(new RemovedStateField(category, field));
                    }
                }
            }
        }
        else
        {
            for (var index = spec.FlatStates.Count - 1; index >= 0; index--)
            {
                var field = spec.FlatStates[index]?.Trim() ?? string.Empty;
                if (HiddenFixedStateFields.Contains(field) || ClassStateCatalog.FindCategory(field) is not null)
                {
                    continue;
                }

                spec.FlatStates.RemoveAt(index);
                if (field.Length > 0)
                {
                    removed.Add(new RemovedStateField(ClassStateCatalog.CategoryState, field));
                }
            }
        }

        removed.Reverse();
        return removed;
    }

    private static bool IsRecognizedStateField(string category, string field)
        => field.Length > 0
           && (ClassStateCatalog.IsKnown(category, field)
               || string.Equals(category, ClassStateCatalog.CategoryState, StringComparison.Ordinal)
               && HiddenFixedStateFields.Contains(field));

    private string ResolveClassPath(int classId)
    {
        var path = Path.Combine(_classDirectory, ClassNames.GetConfigFileName(classId) + ".lua");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"找不到职业配置文件: {path}", path);
        }

        if (!File.Exists(_classMacrosPath))
        {
            throw new FileNotFoundException($"找不到职业宏文件: {_classMacrosPath}", _classMacrosPath);
        }

        return path;
    }

    private static ModuleSpecSnapshot CaptureSpec(ClassBlocksStore.SpecBlocks spec) => new()
    {
        NestedStates = spec.NestedStates,
        FlatStates = new List<string>(spec.FlatStates),
        CategorizedStates = spec.CategorizedStates.ToDictionary(
            pair => pair.Key,
            pair => new List<string>(pair.Value),
            StringComparer.Ordinal),
        Items = spec.Items
            .Where(item => item.ItemId is > 0 && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ModuleItemSnapshot
            {
                ItemId = item.ItemId!.Value,
                Name = item.Name,
                IsEquipped = item.IsEquipped
            })
            .ToList(),
        PlayerAuras = spec.PlayerAuras.Select(CaptureAura).ToList(),
        TargetHarmfulAuras = spec.TargetHarmfulAuras.Select(CaptureAura).ToList(),
        TargetHelpfulAuras = spec.TargetHelpfulAuras.Select(CaptureAura).ToList(),
        FocusHarmfulAuras = spec.FocusHarmfulAuras.Select(CaptureAura).ToList(),
        FocusHelpfulAuras = spec.FocusHelpfulAuras.Select(CaptureAura).ToList(),
        Spells = spec.Spells.Select(entry => new ModuleSpellSnapshot
        {
            Name = entry.Name,
            SpellId = entry.SpellId,
            Charge = entry.Charge,
            MaxCharge = entry.MaxCharge,
            CastCount = entry.CastCount,
            ForcedKnown = entry.ForcedKnown,
            InSpellBook = entry.InSpellBook
        }).ToList(),
        Group = spec.Group is null ? null : new ModuleGroupSnapshot
        {
            Num = spec.Group.Num,
            HealthPercent = spec.Group.HealthPercent,
            Role = spec.Group.Role,
            Dispel = spec.Group.Dispel,
            Auras = spec.Group.Auras.Select(entry => new ModuleGroupAuraSnapshot
            {
                Offset = entry.Offset,
                Name = entry.Name,
                SpellId = entry.SpellId,
                SpellIds = new List<long>(entry.SpellIds)
            }).ToList()
        }
    };

    private static ModuleAuraSnapshot CaptureAura(ClassBlocksStore.AuraEntry entry) => new()
    {
        Name = entry.Name,
        SpellId = entry.SpellId,
        SpellIds = new List<long>(entry.SpellIds),
        MaxApps = entry.MaxApps
    };

    private static ModuleMacroEntrySnapshot CaptureMacro(ClassMacrosStore.ArrayEntry entry) => new()
    {
        Text = entry.Text,
        Comment = entry.Comment
    };

    private static void MergeSpec(
        ClassBlocksStore.SpecBlocks local,
        ModuleSpecSnapshot incoming,
        MergeCounters counters)
    {
        if (local.NestedStates)
        {
            if (incoming.NestedStates)
            {
                foreach (var category in StateCategories)
                {
                    MergeStrings(local.CategorizedStates[category], incoming.CategorizedStates.GetValueOrDefault(category) ?? [], counters);
                }
            }
            else
            {
                MergeStrings(local.CategorizedStates[ClassStateCatalog.CategoryState], incoming.FlatStates, counters);
            }
        }
        else
        {
            if (incoming.NestedStates)
            {
                foreach (var category in StateCategories)
                {
                    MergeStrings(local.FlatStates, incoming.CategorizedStates.GetValueOrDefault(category) ?? [], counters);
                }
            }
            else
            {
                MergeStrings(local.FlatStates, incoming.FlatStates, counters);
            }
        }

        ClassBlocksStore.RelocateSpecialStateFields(local);

        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        if (local.NestedStates)
        {
            foreach (var category in new[]
                     {
                         ClassStateCatalog.CategoryState,
                         ClassStateCatalog.CategorySpecial,
                         ClassStateCatalog.CategoryResource,
                         ClassStateCatalog.CategoryConfig
                     })
            {
                reservedNames.UnionWith(local.CategorizedStates.GetValueOrDefault(category) ?? []);
            }
        }
        else
        {
            reservedNames.UnionWith(local.FlatStates);
        }
        MergeItems(local.Items, incoming.Items ?? [], reservedNames, counters);

        MergeAuras(local.PlayerAuras, incoming.PlayerAuras, "玩家光环", counters);
        MergeAuras(local.TargetHarmfulAuras, incoming.TargetHarmfulAuras, "目标减益", counters);
        MergeAuras(local.TargetHelpfulAuras, incoming.TargetHelpfulAuras, "目标增益", counters);
        MergeAuras(local.FocusHarmfulAuras, incoming.FocusHarmfulAuras, "焦点减益", counters);
        MergeAuras(local.FocusHelpfulAuras, incoming.FocusHelpfulAuras, "焦点增益", counters);
        MergeSpells(local.Spells, incoming.Spells, counters);
        // 队伍配置属于本地扫描布局；模块快照只为文件兼容保留，导入时不得比较或修改。
    }

    private static void MergeStrings(List<string> local, IEnumerable<string> incoming, MergeCounters counters)
    {
        var existing = new HashSet<string>(local, StringComparer.Ordinal);
        foreach (var value in incoming.Select(item => item?.Trim() ?? string.Empty).Where(item => item.Length > 0))
        {
            if (existing.Add(value))
            {
                local.Add(value);
                counters.ConfigAdded++;
            }
        }
    }

    private static void MergeAuras(
        List<ClassBlocksStore.AuraEntry> local,
        IEnumerable<ModuleAuraSnapshot> incoming,
        string label,
        MergeCounters counters)
    {
        CompactLocalAuras(local, label, counters);

        foreach (var entry in incoming)
        {
            var existing = local.FirstOrDefault(item => AuraIdentityMatches(item, entry));
            if (existing is not null)
            {
                MergeAuraMetadata(existing, entry, label, counters);
                continue;
            }

            var added = new ClassBlocksStore.AuraEntry
            {
                Name = entry.Name,
                SpellId = entry.SpellId,
                MaxApps = entry.MaxApps
            };
            added.SpellIds.AddRange(entry.SpellIds);
            local.Add(added);
            counters.ConfigAdded++;
        }
    }

    private static void CompactLocalAuras(
        List<ClassBlocksStore.AuraEntry> local,
        string label,
        MergeCounters counters)
    {
        for (var index = 0; index < local.Count; index++)
        {
            var current = local[index];
            var existing = local.Take(index).FirstOrDefault(item => AuraIdentityMatches(item, current));
            if (existing is null)
            {
                continue;
            }

            MergeAuraMetadata(existing, current, label, counters);
            local.RemoveAt(index--);
            counters.ConfigUpdated++;
        }
    }

    private static bool AuraIdentityMatches(
        ClassBlocksStore.AuraEntry left,
        ClassBlocksStore.AuraEntry right)
    {
        return SpellIdentityMatches(
            GetAuraSpellIds(left.SpellId, left.SpellIds),
            left.Name,
            GetAuraSpellIds(right.SpellId, right.SpellIds),
            right.Name);
    }

    private static bool AuraIdentityMatches(
        ClassBlocksStore.AuraEntry left,
        ModuleAuraSnapshot right)
    {
        return SpellIdentityMatches(
            GetAuraSpellIds(left.SpellId, left.SpellIds),
            left.Name,
            GetAuraSpellIds(right.SpellId, right.SpellIds),
            right.Name);
    }

    private static void MergeAuraMetadata(
        ClassBlocksStore.AuraEntry target,
        ClassBlocksStore.AuraEntry incoming,
        string label,
        MergeCounters counters)
    {
        MergeAuraMetadataCore(
            target,
            incoming.Name,
            incoming.SpellId,
            incoming.SpellIds,
            incoming.MaxApps,
            label,
            counters);
    }

    private static void MergeAuraMetadata(
        ClassBlocksStore.AuraEntry target,
        ModuleAuraSnapshot incoming,
        string label,
        MergeCounters counters)
    {
        MergeAuraMetadataCore(
            target,
            incoming.Name,
            incoming.SpellId,
            incoming.SpellIds,
            incoming.MaxApps,
            label,
            counters);
    }

    private static void MergeAuraMetadataCore(
        ClassBlocksStore.AuraEntry target,
        string? incomingName,
        long? incomingSpellId,
        IEnumerable<long>? incomingSpellIds,
        int? incomingMaxApps,
        string label,
        MergeCounters counters)
    {
        var changed = false;
        if (target.SpellId is null && incomingSpellId is > 0)
        {
            target.SpellId = incomingSpellId;
            changed = true;
        }

        foreach (var spellId in GetAuraSpellIds(incomingSpellId, incomingSpellIds))
        {
            if (target.SpellId != spellId && !target.SpellIds.Contains(spellId))
            {
                target.SpellIds.Add(spellId);
                changed = true;
            }
        }

        if (target.MaxApps is null && incomingMaxApps is not null)
        {
            target.MaxApps = incomingMaxApps;
            changed = true;
        }
        else if (target.MaxApps is not null
                 && incomingMaxApps is not null
                 && target.MaxApps != incomingMaxApps)
        {
            counters.Conflicts.Add(
                $"{label}“{DisplayName(incomingName, incomingSpellId)}”的 maxApps 存在差异："
                + $"本地 {target.MaxApps}、模块 {incomingMaxApps}，已保留本地。");
        }

        if (string.IsNullOrWhiteSpace(target.Name) && !string.IsNullOrWhiteSpace(incomingName))
        {
            target.Name = incomingName;
            changed = true;
        }

        if (changed)
        {
            counters.ConfigUpdated++;
        }
    }

    private static void MergeSpells(
        List<ClassBlocksStore.SpellEntry> local,
        IEnumerable<ModuleSpellSnapshot> incoming,
        MergeCounters counters)
    {
        CompactLocalSpells(local, counters);

        foreach (var entry in incoming)
        {
            var name = DisplayName(entry.Name, entry.SpellId);
            var existing = local.FirstOrDefault(item => SpellIdentityMatches(item, entry));
            if (existing is not null)
            {
                MergeSpellMetadata(existing, entry, name, counters);
                continue;
            }

            local.Add(new ClassBlocksStore.SpellEntry
            {
                Name = entry.Name,
                SpellId = entry.SpellId,
                Charge = entry.Charge,
                MaxCharge = entry.MaxCharge,
                CastCount = entry.CastCount,
                ForcedKnown = entry.ForcedKnown,
                InSpellBook = entry.InSpellBook
            });
            counters.ConfigAdded++;
        }
    }

    private static void CompactLocalSpells(
        List<ClassBlocksStore.SpellEntry> local,
        MergeCounters counters)
    {
        for (var index = 0; index < local.Count; index++)
        {
            var current = local[index];
            var existing = local.Take(index).FirstOrDefault(item => SpellIdentityMatches(item, current));
            if (existing is null)
            {
                continue;
            }

            MergeSpellMetadata(existing, current, DisplayName(current.Name, current.SpellId), counters);
            local.RemoveAt(index--);
            counters.ConfigUpdated++;
        }
    }

    private static void MergeItems(
        List<ClassBlocksStore.ItemEntry> local,
        IEnumerable<ModuleItemSnapshot> incoming,
        IReadOnlySet<string> reservedNames,
        MergeCounters counters)
    {
        CompactLocalItems(local, counters);

        foreach (var item in incoming.OrderBy(entry => entry.ItemId))
        {
            if (!IsValidItemId(item.ItemId))
            {
                continue;
            }

            var byId = local.FirstOrDefault(existing => existing.ItemId == item.ItemId);
            if (byId is not null)
            {
                MergeItemMetadata(byId, item, counters);
                continue;
            }

            var byName = local.FirstOrDefault(existing =>
                string.Equals(existing.Name, item.Name, StringComparison.Ordinal));
            if (byName is not null)
            {
                counters.Conflicts.Add(
                    $"物品名称“{item.Name}”的 itemId 不同：本地 {byName.ItemId}、模块 {item.ItemId}，已保留本地。");
                continue;
            }

            if (reservedNames.Contains(item.Name))
            {
                counters.Conflicts.Add($"物品名称“{item.Name}”与本地状态、特殊、能量或配置开关字段重名，已跳过。");
                continue;
            }

            local.Add(new ClassBlocksStore.ItemEntry
            {
                ItemId = item.ItemId,
                Name = item.Name,
                IsEquipped = item.IsEquipped
            });
            counters.ConfigAdded++;
        }

        local.Sort((left, right) => (left.ItemId ?? long.MaxValue).CompareTo(right.ItemId ?? long.MaxValue));
    }

    private static void CompactLocalItems(
        List<ClassBlocksStore.ItemEntry> local,
        MergeCounters counters)
    {
        for (var index = 0; index < local.Count; index++)
        {
            var current = local[index];
            if (current.ItemId is not > 0)
            {
                continue;
            }

            var existing = local.Take(index).FirstOrDefault(item => item.ItemId == current.ItemId);
            if (existing is null)
            {
                continue;
            }

            MergeItemMetadata(existing, current, counters);
            local.RemoveAt(index--);
            counters.ConfigUpdated++;
        }
    }

    private static void MergeItemMetadata(
        ClassBlocksStore.ItemEntry target,
        ModuleItemSnapshot incoming,
        MergeCounters counters)
        => MergeItemMetadataCore(target, incoming.Name, incoming.IsEquipped, counters);

    private static void MergeItemMetadata(
        ClassBlocksStore.ItemEntry target,
        ClassBlocksStore.ItemEntry incoming,
        MergeCounters counters)
        => MergeItemMetadataCore(target, incoming.Name, incoming.IsEquipped, counters);

    private static void MergeItemMetadataCore(
        ClassBlocksStore.ItemEntry target,
        string? incomingName,
        bool incomingIsEquipped,
        MergeCounters counters)
    {
        var changed = false;
        if (!target.IsEquipped && incomingIsEquipped)
        {
            target.IsEquipped = true;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(target.Name) && !string.IsNullOrWhiteSpace(incomingName))
        {
            target.Name = incomingName.Trim();
            changed = true;
        }
        else if (!string.IsNullOrWhiteSpace(target.Name)
                 && !string.IsNullOrWhiteSpace(incomingName)
                 && !string.Equals(target.Name, incomingName.Trim(), StringComparison.Ordinal))
        {
            counters.Conflicts.Add(
                $"物品 itemId {target.ItemId} 的名称不同：本地“{target.Name}”、模块“{incomingName.Trim()}”，已保留本地。");
        }

        if (changed)
        {
            counters.ConfigUpdated++;
        }
    }

    private static void MergeSpellMetadata(
        ClassBlocksStore.SpellEntry target,
        ModuleSpellSnapshot incoming,
        string name,
        MergeCounters counters)
        => MergeSpellMetadataCore(
            target,
            incoming.Name,
            incoming.Charge,
            incoming.MaxCharge,
            incoming.CastCount,
            incoming.ForcedKnown,
            incoming.InSpellBook,
            name,
            counters);

    private static void MergeSpellMetadata(
        ClassBlocksStore.SpellEntry target,
        ClassBlocksStore.SpellEntry incoming,
        string name,
        MergeCounters counters)
        => MergeSpellMetadataCore(
            target,
            incoming.Name,
            incoming.Charge,
            incoming.MaxCharge,
            incoming.CastCount,
            incoming.ForcedKnown,
            incoming.InSpellBook,
            name,
            counters);

    private static void MergeSpellMetadataCore(
        ClassBlocksStore.SpellEntry target,
        string? incomingName,
        bool incomingCharge,
        int? incomingMaxCharge,
        int? incomingCastCount,
        bool incomingForcedKnown,
        bool incomingInSpellBook,
        string name,
        MergeCounters counters)
    {
        var changed = false;
        if (!target.Charge && incomingCharge)
        {
            target.Charge = true;
            changed = true;
        }
        if (!target.ForcedKnown && incomingForcedKnown)
        {
            target.ForcedKnown = true;
            changed = true;
        }
        if (!target.InSpellBook && incomingInSpellBook)
        {
            target.InSpellBook = true;
            changed = true;
        }

        MergeNullable(
            target.MaxCharge,
            incomingMaxCharge,
            value => target.MaxCharge = value,
            "最大充能");
        MergeNullable(
            target.CastCount,
            incomingCastCount,
            value => target.CastCount = value,
            "施法次数");

        if (string.IsNullOrWhiteSpace(target.Name) && !string.IsNullOrWhiteSpace(incomingName))
        {
            target.Name = incomingName.Trim();
            changed = true;
        }

        if (changed)
        {
            counters.ConfigUpdated++;
        }

        void MergeNullable(int? localValue, int? incomingValue, Action<int> assign, string field)
        {
            if (localValue is null && incomingValue is { } supplied)
            {
                assign(supplied);
                changed = true;
            }
            else if (localValue is not null && incomingValue is not null && localValue != incomingValue)
            {
                counters.Conflicts.Add(
                    $"法术“{name}”的{field}存在差异：本地 {localValue}、模块 {incomingValue}，已保留本地。");
            }
        }
    }

    private static bool SpellIdentityMatches(
        ClassBlocksStore.SpellEntry left,
        ClassBlocksStore.SpellEntry right)
    {
        return IsValidSpellId(left.SpellId) && left.SpellId == right.SpellId;
    }

    private static bool SpellIdentityMatches(
        ClassBlocksStore.SpellEntry left,
        ModuleSpellSnapshot right)
    {
        return IsValidSpellId(left.SpellId) && left.SpellId == right.SpellId;
    }

    private static bool SpellIdentityMatches(
        IEnumerable<long> leftIds,
        string? leftName,
        IEnumerable<long> rightIds,
        string? rightName)
    {
        var leftIdSet = leftIds.Where(IsValidSpellId).ToHashSet();
        var rightIdSet = rightIds.Where(IsValidSpellId).ToHashSet();
        return leftIdSet.Count > 0
            && rightIdSet.Count > 0
            && leftIdSet.Overlaps(rightIdSet);
    }

    private static IEnumerable<long> GetSpellIds(long spellId)
        => IsValidSpellId(spellId) ? [spellId] : [];

    private static IEnumerable<long> GetAuraSpellIds(long? spellId, IEnumerable<long>? spellIds)
    {
        var result = new HashSet<long>();
        if (spellId is { } primary && IsValidSpellId(primary))
        {
            result.Add(primary);
        }

        foreach (var id in spellIds ?? [])
        {
            if (IsValidSpellId(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static bool IsValidSpellId(long spellId) => spellId > 0;

    private static bool IsValidItemId(long itemId) => itemId > 0;

    private static string DisplayName(string? name, long? spellId)
        => string.IsNullOrWhiteSpace(name) ? spellId?.ToString() ?? "未命名" : name.Trim();

    private static void MergeSpellsList(
        List<ClassBlocksStore.SpellsListEntry> local,
        IEnumerable<ModuleSpellListEntrySnapshot> incoming,
        MergeCounters counters)
    {
        CompactLocalSpellsList(local, counters);

        foreach (var entry in incoming)
        {
            var byId = local.FirstOrDefault(item => item.SpellId == entry.SpellId);
            if (byId is not null)
            {
                // spellId 是跨模块稳定标识；索引是当前职业文件的本地编码。
                // 同一 spellId 已存在时保留本地索引/名称，不因来源索引不同产生冲突。
                continue;
            }

            local.Add(new ClassBlocksStore.SpellsListEntry
            {
                SpellId = entry.SpellId,
                Index = entry.Index,
                Name = entry.Name,
                OriginalSpellId = 0
            });
            counters.ConfigAdded++;
        }
    }

    private static void CompactLocalSpellsList(
        List<ClassBlocksStore.SpellsListEntry> local,
        MergeCounters counters)
    {
        var seen = new HashSet<long>();
        for (var index = 0; index < local.Count; index++)
        {
            if (seen.Add(local[index].SpellId))
            {
                continue;
            }

            local.RemoveAt(index--);
            counters.ConfigUpdated++;
        }
    }

    private static void MergeItemsList(
        List<ClassBlocksStore.ItemsListEntry> local,
        IEnumerable<ModuleItemListEntrySnapshot>? incoming,
        MergeCounters counters)
    {
        CompactLocalItemsList(local, counters);

        foreach (var entry in incoming ?? [])
        {
            if (!IsValidItemId(entry.ItemId))
            {
                continue;
            }

            var byId = local.FirstOrDefault(item => item.ItemId == entry.ItemId);
            if (byId is not null)
            {
                // itemId 是跨模块稳定标识；索引是当前职业文件的本地编码。
                // 同一 itemId 已存在时保留本地索引/名称，不因来源索引不同产生冲突。
                continue;
            }

            local.Add(new ClassBlocksStore.ItemsListEntry
            {
                ItemId = entry.ItemId,
                Index = entry.Index,
                Name = entry.Name,
                OriginalItemId = 0
            });
            counters.ConfigAdded++;
        }
    }

    private static void CompactLocalItemsList(
        List<ClassBlocksStore.ItemsListEntry> local,
        MergeCounters counters)
    {
        var seen = new HashSet<long>();
        for (var index = 0; index < local.Count; index++)
        {
            if (seen.Add(local[index].ItemId))
            {
                continue;
            }

            local.RemoveAt(index--);
            counters.ConfigUpdated++;
        }
    }

    private static void MergeMacros(
        ClassMacrosStore.ClassMacros local,
        int classId,
        ModuleMacrosSnapshot incoming,
        MergeCounters counters)
    {
        var commonNames = new HashSet<string>(local.DynamicSpells.Select(NormalizeMacroText), StringComparer.Ordinal);
        foreach (var value in incoming.DynamicCommon ?? [])
        {
            var normalized = NormalizeMacroText(value);
            if (normalized.Length > 0 && commonNames.Add(normalized))
            {
                local.DynamicSpells.Add(value.Trim());
                counters.MacrosAdded++;
            }
        }

        // 历史模块的专精动态宏只对牧师/圣骑士迁移到职业宏；其他职业按新规则丢弃。
        if (incoming.UsesSpecDynamicSpells && classId is 2 or 5)
        {
            foreach (var value in incoming.DynamicForSpec ?? [])
            {
                var normalized = NormalizeMacroText(value);
                if (normalized.Length > 0 && commonNames.Add(normalized))
                {
                    local.DynamicSpells.Add(value.Trim());
                    counters.MacrosAdded++;
                }
            }
        }

        MergeMacroEntries(local.StaticSpells, incoming.StaticSpells, isSpecial: false, counters);
        MergeMacroEntries(local.SpecialSpells, incoming.SpecialSpells, isSpecial: true, counters);
    }

    private static void MergeMacroEntries(
        List<ClassMacrosStore.ArrayEntry> local,
        IEnumerable<ModuleMacroEntrySnapshot> incoming,
        bool isSpecial,
        MergeCounters counters)
    {
        foreach (var entry in CompactMacroSnapshots(incoming, isSpecial))
        {
            var overlapIndex = IndexOfOverlappingArrayEntry(local, entry, isSpecial);
            if (overlapIndex >= 0)
            {
                var existing = local[overlapIndex];
                if (string.IsNullOrWhiteSpace(existing.Comment) && !string.IsNullOrWhiteSpace(entry.Comment))
                {
                    existing.Comment = entry.Comment;
                    counters.MacrosAdded++;
                    continue;
                }

                if (!string.Equals(NormalizeMacroText(existing.Text), NormalizeMacroText(entry.Text), StringComparison.Ordinal)
                    || CommentsConflict(existing.Comment, entry.Comment))
                {
                    var identity = GetMacroIdentity(entry.Text, entry.Comment, isSpecial);
                    counters.Conflicts.Add($"宏“{identity.Spell}”与本地内容不同，已保留本地。");
                }

                continue;
            }

            local.Add(new ClassMacrosStore.ArrayEntry { Text = entry.Text, Comment = entry.Comment });
            counters.MacrosAdded++;
        }
    }

    private static List<ModuleMacroEntrySnapshot> CompactMacroSnapshots(
        IEnumerable<ModuleMacroEntrySnapshot>? entries,
        bool isSpecial)
    {
        var result = new List<ModuleMacroEntrySnapshot>();
        foreach (var entry in entries ?? [])
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Text))
            {
                continue;
            }

            var index = IndexOfOverlappingMacroSnapshot(result, entry, isSpecial);
            if (index >= 0)
            {
                UpgradeMacroComment(result[index], entry);
                continue;
            }

            result.Add(entry.Clone());
        }

        return result;
    }

    private static void UpgradeMacroComment(ModuleMacroEntrySnapshot target, ModuleMacroEntrySnapshot source)
    {
        if (string.IsNullOrWhiteSpace(target.Comment) && !string.IsNullOrWhiteSpace(source.Comment))
        {
            target.Comment = source.Comment;
        }
    }

    private static int IndexOfOverlappingMacroSnapshot(
        IReadOnlyList<ModuleMacroEntrySnapshot> entries,
        ModuleMacroEntrySnapshot candidate,
        bool isSpecial)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (MacrosOverlap(entries[i].Text, entries[i].Comment, candidate.Text, candidate.Comment, isSpecial))
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfOverlappingArrayEntry(
        IReadOnlyList<ClassMacrosStore.ArrayEntry> entries,
        ModuleMacroEntrySnapshot candidate,
        bool isSpecial)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (MacrosOverlap(entries[i].Text, entries[i].Comment, candidate.Text, candidate.Comment, isSpecial))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool MacrosOverlap(
        string leftText,
        string? leftComment,
        string rightText,
        string? rightComment,
        bool isSpecial)
    {
        var leftNormalized = NormalizeMacroText(leftText);
        var rightNormalized = NormalizeMacroText(rightText);
        if (leftNormalized.Length > 0 && leftNormalized == rightNormalized)
        {
            return true;
        }

        if (!isSpecial)
        {
            return false;
        }

        var leftName = leftComment?.Trim() ?? string.Empty;
        var rightName = rightComment?.Trim() ?? string.Empty;
        return leftName.Length > 0 && string.Equals(leftName, rightName, StringComparison.Ordinal);
    }

    private static bool CommentsConflict(string? left, string? right)
    {
        var leftName = left?.Trim() ?? string.Empty;
        var rightName = right?.Trim() ?? string.Empty;
        return leftName.Length > 0
               && rightName.Length > 0
               && !string.Equals(leftName, rightName, StringComparison.Ordinal);
    }

    private static MacroIdentity GetMacroIdentity(string text, string? comment, bool isSpecial)
    {
        var parsed = isSpecial
            ? FuyutsuiKeymapConverter.ParseSpecialMacro(text, comment)
            : FuyutsuiKeymapConverter.ParseStaticMacro(text, comment);
        var spell = parsed.Spell.Trim();
        return spell.Length > 0
            ? new MacroIdentity(isSpecial, parsed.Unit, spell, parsed.Condition)
            : new MacroIdentity(isSpecial, parsed.Unit, NormalizeMacroText(text), parsed.Condition);
    }

    private static string NormalizeMacroText(string? value)
        => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static void EnsureMacroCapacity(int classId, ClassMacrosStore.ClassMacros macros)
    {
        var slots = checked(macros.DynamicSpells.Count * 30 + macros.StaticSpells.Count + macros.SpecialSpells.Count);
        if (slots > FuyutsuiKeymapConverter.MacroSlotCapacity)
        {
            var className = ClassNames.GetClassAndSpecName(classId, null).ClassName ?? $"职业{classId}";
            throw new InvalidOperationException(
                $"宏容量超限：{className} 合并后 {slots} 个槽位，最大 {FuyutsuiKeymapConverter.MacroSlotCapacity}。模块未导入。");
        }
    }

    private static void CommitDocuments(
        ClassBlocksStore.ClassFileDocument config,
        ClassMacrosStore.MacrosDocument macros,
        bool saveConfig,
        bool saveMacros)
    {
        var originalConfig = saveConfig ? File.ReadAllText(config.FilePath, Encoding.UTF8) : null;
        var originalMacros = saveMacros ? File.ReadAllText(macros.FilePath, Encoding.UTF8) : null;
        try
        {
            if (saveConfig)
            {
                ClassBlocksStore.Save(config);
            }
            if (saveMacros)
            {
                ClassMacrosStore.Save(macros);
            }
        }
        catch (Exception saveError)
        {
            var rollbackErrors = new List<Exception>();
            TryRestore(config.FilePath, originalConfig, rollbackErrors);
            TryRestore(macros.FilePath, originalMacros, rollbackErrors);
            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException("模块依赖写入失败，且回滚未完全成功。", [saveError, .. rollbackErrors]);
            }
            throw;
        }
    }

    private static void TryRestore(string path, string? contents, ICollection<Exception> errors)
    {
        if (contents is null)
        {
            return;
        }
        try
        {
            AtomicFile.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static bool SpellEquals(ClassBlocksStore.SpellEntry left, ModuleSpellSnapshot right)
        => left.SpellId == right.SpellId
           && left.Charge == right.Charge
           && left.MaxCharge == right.MaxCharge
           && left.CastCount == right.CastCount
           && left.ForcedKnown == right.ForcedKnown
           && left.InSpellBook == right.InSpellBook;

    private sealed class MergeCounters
    {
        public int ConfigAdded { get; set; }
        public int ConfigUpdated { get; set; }
        public int MacrosAdded { get; set; }
        public List<string> Conflicts { get; } = new();

        public bool HasConfigChanges => ConfigAdded > 0 || ConfigUpdated > 0;
    }

    // 仅用于冲突提示：静态宏按解析出的目标/技能/条件；特殊宏按手工技能名。
    private readonly record struct MacroIdentity(bool IsSpecial, int Unit, string Spell, string Condition);
}

internal sealed class ModuleDependencyImportResult
{
    public int ConfigAdded { get; set; }
    public int ConfigUpdated { get; set; }
    public int MacrosAdded { get; set; }
    public List<string> ChangedModules { get; } = new();
    public List<string> Conflicts { get; } = new();
    public HashSet<string> ConflictedModuleIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RejectedModuleDependency> Rejected { get; } = new();
    public List<ModuleDefinition> SanitizedModules { get; } = new();
    public List<RemovedModuleStateField> RemovedStateFields { get; } = new();
    public bool HasChanges => ConfigAdded > 0 || ConfigUpdated > 0 || MacrosAdded > 0;
}

internal sealed record RejectedModuleDependency(string ModuleId, string ModuleName, string Reason);
internal sealed record RemovedModuleStateField(string ModuleName, string Category, string Name);
internal sealed record RemovedStateField(string Category, string Name);
