using System.Text;
namespace Shigure;

internal sealed class ModuleDependencyService
{
    private static readonly string[] StateCategories =
    [
        ClassStateCatalog.CategoryState,
        ClassStateCatalog.CategoryResource,
        ClassStateCatalog.CategoryItem,
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
                }).ToList()
            },
            Macros = new ModuleMacrosSnapshot
            {
                UsesSpecDynamicSpells = macros.UsesSpecDynamicSpells,
                DynamicCommon = new List<string>(macros.DynamicCommon),
                DynamicForSpec = macros.UsesSpecDynamicSpells
                    ? new List<string>(macros.DynamicBySpec.GetValueOrDefault(specId.Value) ?? [])
                    : [],
                StaticSpells = macros.StaticSpells.Select(CaptureMacro).ToList(),
                SpecialSpells = macros.SpecialSpells.Select(CaptureMacro).ToList()
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
        ValidateSnapshot(module, snapshot);

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
        MergeMacros(localMacros, snapshot.SpecId, snapshot.Macros, counters);
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
        if (snapshot.SchemaVersion != ModuleDependencySnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持依赖快照版本 {snapshot.SchemaVersion}。");
        }

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
    }

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

        MergeAuras(local.PlayerAuras, incoming.PlayerAuras, "玩家光环", counters);
        MergeAuras(local.TargetHarmfulAuras, incoming.TargetHarmfulAuras, "目标减益", counters);
        MergeAuras(local.TargetHelpfulAuras, incoming.TargetHelpfulAuras, "目标增益", counters);
        MergeAuras(local.FocusHarmfulAuras, incoming.FocusHarmfulAuras, "焦点减益", counters);
        MergeAuras(local.FocusHelpfulAuras, incoming.FocusHelpfulAuras, "焦点增益", counters);
        MergeSpells(local.Spells, incoming.Spells, counters);
        MergeGroup(local, incoming.Group, counters);
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
            var mergedMaxApps = Math.Max(target.MaxApps.Value, incomingMaxApps.Value);
            counters.Conflicts.Add(
                $"{label}“{DisplayName(incomingName, incomingSpellId)}”的 maxApps 存在差异："
                + $"{target.MaxApps}、{incomingMaxApps}，已采用 {mergedMaxApps}。");
            if (target.MaxApps != mergedMaxApps)
            {
                target.MaxApps = mergedMaxApps;
                changed = true;
            }
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
                if (!SpellEquals(existing, entry))
                {
                    counters.Conflicts.Add($"法术“{name}”与本地内容不同，已保留本地。");
                }
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

            local.RemoveAt(index--);
            counters.ConfigUpdated++;
        }
    }

    private static bool SpellIdentityMatches(
        ClassBlocksStore.SpellEntry left,
        ClassBlocksStore.SpellEntry right)
    {
        return SpellIdentityMatches(
            GetSpellIds(left.SpellId),
            left.Name,
            GetSpellIds(right.SpellId),
            right.Name);
    }

    private static bool SpellIdentityMatches(
        ClassBlocksStore.SpellEntry left,
        ModuleSpellSnapshot right)
    {
        return SpellIdentityMatches(
            GetSpellIds(left.SpellId),
            left.Name,
            GetSpellIds(right.SpellId),
            right.Name);
    }

    private static bool SpellIdentityMatches(
        IEnumerable<long> leftIds,
        string? leftName,
        IEnumerable<long> rightIds,
        string? rightName)
    {
        var leftIdSet = leftIds.Where(IsValidSpellId).ToHashSet();
        var rightIdSet = rightIds.Where(IsValidSpellId).ToHashSet();
        if (leftIdSet.Count > 0 || rightIdSet.Count > 0)
        {
            return leftIdSet.Count > 0
                && rightIdSet.Count > 0
                && leftIdSet.Overlaps(rightIdSet);
        }

        return string.Equals(leftName?.Trim(), rightName?.Trim(), StringComparison.Ordinal);
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
            var byIndex = local.FirstOrDefault(item => item.Index == entry.Index);
            if (byId is not null || byIndex is not null)
            {
                var existing = byId ?? byIndex!;
                if (existing.SpellId != entry.SpellId
                    || existing.Index != entry.Index
                    || !string.Equals(existing.Name, entry.Name, StringComparison.Ordinal))
                {
                    counters.Conflicts.Add($"一键法术 {entry.Index}/{entry.SpellId} 与本地索引或 SpellId 冲突，已保留本地。");
                }
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

    private static void MergeGroup(
        ClassBlocksStore.SpecBlocks localSpec,
        ModuleGroupSnapshot? incoming,
        MergeCounters counters)
    {
        if (incoming is null)
        {
            return;
        }

        if (localSpec.Group is null)
        {
            var group = new ClassBlocksStore.GroupBlocks
            {
                Num = incoming.Num,
                HealthPercent = incoming.HealthPercent,
                Role = incoming.Role,
                Dispel = incoming.Dispel
            };
            counters.ConfigAdded++;
            foreach (var aura in incoming.Auras)
            {
                var existing = group.Auras.FirstOrDefault(item => GroupAuraIdentityMatches(item, aura));
                if (existing is not null)
                {
                    MergeGroupAuraMetadata(existing, aura, counters);
                    continue;
                }

                group.Auras.Add(ToGroupAura(aura, aura.Offset));
                counters.ConfigAdded++;
            }
            localSpec.Group = group;
            return;
        }

        var local = localSpec.Group;
        CompactLocalGroupAuras(local.Auras, counters);
        var occupied = new HashSet<int>(local.Auras.Select(aura => aura.Offset));
        AddOffset(local.HealthPercent);
        AddOffset(local.Role);
        AddOffset(local.Dispel);

        if (local.HealthPercent is null && incoming.HealthPercent is not null)
        {
            local.HealthPercent = AllocateOffset(incoming.HealthPercent.Value, occupied);
            counters.ConfigAdded++;
        }
        if (local.Role is null && incoming.Role is not null)
        {
            local.Role = AllocateOffset(incoming.Role.Value, occupied);
            counters.ConfigAdded++;
        }
        if (local.Dispel is null && incoming.Dispel is not null)
        {
            local.Dispel = AllocateOffset(incoming.Dispel.Value, occupied);
            counters.ConfigAdded++;
        }

        foreach (var aura in incoming.Auras)
        {
            var existing = local.Auras.FirstOrDefault(item => GroupAuraIdentityMatches(item, aura));
            if (existing is not null)
            {
                MergeGroupAuraMetadata(existing, aura, counters);
                continue;
            }

            var offset = AllocateOffset(aura.Offset, occupied);
            local.Auras.Add(ToGroupAura(aura, offset));
            counters.ConfigAdded++;
        }

        if (occupied.Count > 0)
        {
            local.Num = Math.Max(local.Num, occupied.Max());
        }

        void AddOffset(int? offset)
        {
            if (offset is > 0)
            {
                occupied.Add(offset.Value);
            }
        }
    }

    private static bool GroupAuraIdentityMatches(
        ClassBlocksStore.GroupAuraEntry left,
        ModuleGroupAuraSnapshot right)
    {
        // offset 是队伍光环的槽位，同一 spellId 出现在不同槽位时仍然是两个槽位。
        return left.Offset == right.Offset
            && SpellIdentityMatches(
                GetAuraSpellIds(left.SpellId, left.SpellIds),
                left.Name,
                GetAuraSpellIds(right.SpellId, right.SpellIds),
                right.Name);
    }

    private static bool GroupAuraIdentityMatches(
        ClassBlocksStore.GroupAuraEntry left,
        ClassBlocksStore.GroupAuraEntry right)
    {
        return left.Offset == right.Offset
            && SpellIdentityMatches(
                GetAuraSpellIds(left.SpellId, left.SpellIds),
                left.Name,
                GetAuraSpellIds(right.SpellId, right.SpellIds),
                right.Name);
    }

    private static void CompactLocalGroupAuras(
        List<ClassBlocksStore.GroupAuraEntry> local,
        MergeCounters counters)
    {
        for (var index = 0; index < local.Count; index++)
        {
            var current = local[index];
            var existing = local.Take(index).FirstOrDefault(item => GroupAuraIdentityMatches(item, current));
            if (existing is null)
            {
                continue;
            }

            if (existing.SpellId is null && current.SpellId is > 0)
            {
                existing.SpellId = current.SpellId;
            }

            foreach (var spellId in GetAuraSpellIds(null, current.SpellIds))
            {
                if (!existing.SpellIds.Contains(spellId))
                {
                    existing.SpellIds.Add(spellId);
                }
            }

            if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(current.Name))
            {
                existing.Name = current.Name;
            }

            local.RemoveAt(index--);
            counters.ConfigUpdated++;
        }
    }

    private static void MergeGroupAuraMetadata(
        ClassBlocksStore.GroupAuraEntry target,
        ModuleGroupAuraSnapshot incoming,
        MergeCounters counters)
    {
        var changed = false;
        if (target.SpellId is null && incoming.SpellId is > 0)
        {
            target.SpellId = incoming.SpellId;
            changed = true;
        }

        foreach (var spellId in GetAuraSpellIds(null, incoming.SpellIds))
        {
            if (!target.SpellIds.Contains(spellId))
            {
                target.SpellIds.Add(spellId);
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(target.Name) && !string.IsNullOrWhiteSpace(incoming.Name))
        {
            target.Name = incoming.Name;
            changed = true;
        }

        if (changed)
        {
            counters.ConfigUpdated++;
        }
    }

    private static int AllocateOffset(int desired, ISet<int> occupied)
    {
        var offset = desired > 0 && !occupied.Contains(desired) ? desired : 1;
        while (occupied.Contains(offset))
        {
            offset++;
        }
        occupied.Add(offset);
        return offset;
    }

    private static ClassBlocksStore.GroupAuraEntry ToGroupAura(ModuleGroupAuraSnapshot entry, int offset)
    {
        var aura = new ClassBlocksStore.GroupAuraEntry
        {
            Offset = offset,
            Name = entry.Name,
            SpellId = entry.SpellId
        };
        aura.SpellIds.AddRange(entry.SpellIds);
        return aura;
    }

    private static void MergeMacros(
        ClassMacrosStore.ClassMacros local,
        int specId,
        ModuleMacrosSnapshot incoming,
        MergeCounters counters)
    {
        var commonNames = new HashSet<string>(local.DynamicCommon.Select(NormalizeMacroText), StringComparer.Ordinal);
        foreach (var value in incoming.DynamicCommon)
        {
            var normalized = NormalizeMacroText(value);
            if (normalized.Length > 0 && commonNames.Add(normalized))
            {
                local.DynamicCommon.Add(value.Trim());
                counters.MacrosAdded++;
            }
        }

        if (incoming.UsesSpecDynamicSpells && incoming.DynamicForSpec.Count > 0)
        {
            local.UsesSpecDynamicSpells = true;
            if (!local.DynamicBySpec.TryGetValue(specId, out var specMacros))
            {
                specMacros = new List<string>();
                local.DynamicBySpec[specId] = specMacros;
            }

            var resolved = new HashSet<string>(local.DynamicCommon.Select(NormalizeMacroText), StringComparer.Ordinal);
            resolved.UnionWith(specMacros.Select(NormalizeMacroText));
            foreach (var value in incoming.DynamicForSpec)
            {
                var normalized = NormalizeMacroText(value);
                if (normalized.Length > 0 && resolved.Add(normalized))
                {
                    specMacros.Add(value.Trim());
                    counters.MacrosAdded++;
                }
            }
        }

        var identities = BuildMacroIdentities(local);
        MergeMacroEntries(local.StaticSpells, incoming.StaticSpells, isSpecial: false, identities, counters);
        MergeMacroEntries(local.SpecialSpells, incoming.SpecialSpells, isSpecial: true, identities, counters);
    }

    private static Dictionary<MacroIdentity, ModuleMacroEntrySnapshot> BuildMacroIdentities(ClassMacrosStore.ClassMacros macros)
    {
        var result = new Dictionary<MacroIdentity, ModuleMacroEntrySnapshot>();
        foreach (var entry in macros.StaticSpells)
        {
            result.TryAdd(GetMacroIdentity(entry.Text, entry.Comment, isSpecial: false), CaptureMacro(entry));
        }
        foreach (var entry in macros.SpecialSpells)
        {
            result.TryAdd(GetMacroIdentity(entry.Text, entry.Comment, isSpecial: true), CaptureMacro(entry));
        }
        return result;
    }

    private static void MergeMacroEntries(
        List<ClassMacrosStore.ArrayEntry> local,
        IEnumerable<ModuleMacroEntrySnapshot> incoming,
        bool isSpecial,
        IDictionary<MacroIdentity, ModuleMacroEntrySnapshot> identities,
        MergeCounters counters)
    {
        foreach (var entry in incoming)
        {
            var identity = GetMacroIdentity(entry.Text, entry.Comment, isSpecial);
            if (identities.TryGetValue(identity, out var existing))
            {
                if (!MacroEntryEquals(existing, entry))
                {
                    counters.Conflicts.Add($"宏“{identity.Spell}”与本地内容不同，已保留本地。");
                }
                continue;
            }

            local.Add(new ClassMacrosStore.ArrayEntry { Text = entry.Text, Comment = entry.Comment });
            identities[identity] = entry;
            counters.MacrosAdded++;
        }
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

    private static string NormalizeMacroText(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static void EnsureMacroCapacity(int classId, ClassMacrosStore.ClassMacros macros)
    {
        foreach (var (specId, specName) in ClassNames.GetSpecs(classId))
        {
            var dynamicCount = macros.UsesSpecDynamicSpells
                ? macros.DynamicCommon.Count + (macros.DynamicBySpec.GetValueOrDefault(specId)?.Count ?? 0)
                : macros.DynamicCommon.Count;
            var slots = checked(dynamicCount * 30 + macros.StaticSpells.Count + macros.SpecialSpells.Count);
            if (slots > FuyutsuiKeymapConverter.MacroSlotCapacity)
            {
                throw new InvalidOperationException(
                    $"宏容量超限：{ClassNames.GetClassAndSpecName(classId, specId).ClassName} {specName} 合并后 {slots} 个槽位，最大 {FuyutsuiKeymapConverter.MacroSlotCapacity}。模块未导入。");
            }
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

    private static bool MacroEntryEquals(ModuleMacroEntrySnapshot left, ModuleMacroEntrySnapshot right)
        => string.Equals(NormalizeMacroText(left.Text), NormalizeMacroText(right.Text), StringComparison.Ordinal)
           && string.Equals(left.Comment?.Trim(), right.Comment?.Trim(), StringComparison.Ordinal);

    private sealed class MergeCounters
    {
        public int ConfigAdded { get; set; }
        public int ConfigUpdated { get; set; }
        public int MacrosAdded { get; set; }
        public List<string> Conflicts { get; } = new();

        public bool HasConfigChanges => ConfigAdded > 0 || ConfigUpdated > 0;
    }

    // 静态宏和特殊宏可以解析成相同的目标/技能/条件，但仍是两个独立槽位。
    // 例如“恶魔变形”和“/castsequence reset=0.5 恶魔变形,x”必须分别去重。
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
    public bool HasChanges => ConfigAdded > 0 || ConfigUpdated > 0 || MacrosAdded > 0;
}

internal sealed record RejectedModuleDependency(string ModuleId, string ModuleName, string Reason);
