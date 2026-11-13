using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using AutoTest.Contracts;
using AutoTest.Core;
using AutoTest.Desktop.Projects;
using AutoTest.Desktop.Testing;
using AutoTest.Projects.ControllerConfig;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using AutoTest.Specialists.Scan;
using AutoTest.Specialists.Wave;
using Prism.Commands;
using Prism.Mvvm;

namespace AutoTest.Desktop.Configuration;

public sealed record ConfigurationNode(string Key, string Label)
{
    public override string ToString() => Label;
}

public sealed class ConfigurationField : BindableBase
{
    private string _value;
    public ConfigurationField(string key, string title, string value, string hint,
        IReadOnlyList<string>? options = null, bool readOnly = false)
    {
        Key = key; Title = title; _value = value; Hint = hint;
        Options = options ?? Array.Empty<string>(); IsReadOnly = readOnly;
    }
    public string Key { get; }
    public string Title { get; }
    public string Hint { get; }
    public IReadOnlyList<string> Options { get; }
    public bool HasOptions => Options.Count > 0;
    public bool IsReadOnly { get; }
    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value ?? "");
    }
}

public sealed class ConfigurationWorkbenchViewModel : BindableBase
{
    private readonly ProjectWorkspaceService _workspace;
    private readonly ProjectFileDialogService _dialogs;
    private ProjectConfigurationSession _session;
    private object _working;
    private ConfigurationNode? _selectedNode;
    private bool _fieldEdited;
    private bool _busy;
    private string _status = "修改参数后选择“应用参数”，再保存到工程。";

    public ConfigurationWorkbenchViewModel(ProjectWorkspaceService workspace,
        ProjectFileDialogService dialogs, ProjectConfigurationSession session)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _session = session;
        _working = session.Document;
        SaveCommand = new DelegateCommand(async () => await SaveAsync(), () => !IsBusy);
        ReloadCommand = new DelegateCommand(async () => await ReloadAsync(), () => !IsBusy);
        ApplyCommand = new DelegateCommand(() => ApplyFields(), () => !IsBusy);
        AddControllerCommand = new DelegateCommand(AddController, () => IsController && !IsBusy);
        AddButtonActionCommand = new DelegateCommand(AddButtonAction, () => IsController && !IsBusy);
        PickWaveSignalCommand = new DelegateCommand(async () => await PickWaveSignalAsync(), () => IsWave && !IsBusy);
        PickKeyCombinationCommand = new DelegateCommand(() => PickKeys("mask"),
            () => IsControllerState && !IsBusy);
        PickSupportedKeysCommand = new DelegateCommand(() => PickKeys("supportedKeys"),
            () => IsControllerOverview && !IsBusy);
        AddStateCommand = new DelegateCommand(AddState, () => IsController && SelectedControllerId.HasValue && !IsBusy);
        SetDefaultCommand = new DelegateCommand(SetDefault, () => IsController && SelectedStateId.HasValue && !IsBusy);
        AddRuleCommand = new DelegateCommand(AddRule, () => IsScan && !IsBusy);
        AddRecipeCommand = new DelegateCommand(AddRecipe, () => IsScan && !IsBusy);
        RemoveEntryCommand = new DelegateCommand(RemoveSelected,
            () => !IsBusy && _selectedNode is not null && _selectedNode.Key != "profile");
        RebuildNodes();
    }

    public ObservableCollection<ConfigurationNode> Nodes { get; } = new();
    public ObservableCollection<ConfigurationField> Fields { get; } = new();
    public string AssetName => _session.AssetName;
    public string DocumentPath => _session.FilePath;
    public int Revision => _session.AssetRevision;
    public bool IsController => _session.Kind == ProjectConfigurationCodec.ControllerKind;
    public bool IsControllerOverview => IsController && _selectedNode?.Key == "profile";
    public bool IsControllerState => IsController && SelectedStateId.HasValue;
    public bool IsScan => _session.Kind == ProjectConfigurationCodec.ScanSettingsKind;
    public bool IsWave => _session.Kind == ProjectConfigurationCodec.WaveSettingsKind;
    public bool IsFlash => _session.Kind == ProjectConfigurationCodec.FlashSettingsKind;
    public string TypeLabel => _session.Kind switch
    {
        ProjectConfigurationCodec.ControllerKind => "控制器与按键配置",
        ProjectConfigurationCodec.WaveSettingsKind => "波形判定配置",
        ProjectConfigurationCodec.FlashSettingsKind => "烧录配置",
        ProjectConfigurationCodec.ScanSettingsKind => "扫码匹配配置",
        _ => "工程配置"
    };
    public string ExecutionBoundary => _session.Kind switch
    {
        ProjectConfigurationCodec.ControllerKind =>
            "先建立 ECU 的功能操作，再编辑各状态的电压要求、Key 组合和动作时序；配置不等于真实电压已测量。",
        ProjectConfigurationCodec.WaveSettingsKind =>
            "从 DBC 选择信号，再填写波形分析条件；采集、分析、判定由测试步骤明确组织，证据不会自动变成参考。",
        ProjectConfigurationCodec.FlashSettingsKind =>
            "这里只保存 ECU/UDS/固件与安全参数，不执行烧录；真实 UDS/Flash 现场执行需另行接通验证。",
        ProjectConfigurationCodec.ScanSettingsKind =>
            "只有已发布且校验通过的规则和配方才会参与扫码匹配；匹配不自动触发测试或烧录。",
        _ => ""
    };
    public bool IsBusy
    {
        get => _busy;
        private set { if (SetProperty(ref _busy, value)) RefreshCommands(); }
    }
    public string StatusMessage { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsDirty => _fieldEdited || !ProjectConfigurationCodec.Encode(_session.Kind, _working)
        .AsSpan().SequenceEqual(ProjectConfigurationCodec.Encode(_session.Kind, _session.Document));
    public ConfigurationNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            if (_fieldEdited && !ApplyFields())
            {
                RaisePropertyChanged(nameof(SelectedNode));
                return;
            }
            if (SetProperty(ref _selectedNode, value))
            {
                PopulateFields();
                RaisePropertyChanged(nameof(IsControllerOverview));
                RaisePropertyChanged(nameof(IsControllerState));
                RefreshCommands();
            }
        }
    }
    private Guid? SelectedControllerId
    {
        get
        {
            var parts = _selectedNode?.Key.Split('|');
            if (parts is { Length: >= 2 } && parts[0] is "controller" or "state" &&
                Guid.TryParse(parts[1], out var value)) return value;
            return null;
        }
    }
    private Guid? SelectedStateId
    {
        get
        {
            var parts = _selectedNode?.Key.Split('|');
            return parts is { Length: 3 } && parts[0] == "state" &&
                Guid.TryParse(parts[2], out var value) ? value : null;
        }
    }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand ReloadCommand { get; }
    public DelegateCommand ApplyCommand { get; }
    public DelegateCommand AddControllerCommand { get; }
    public DelegateCommand AddButtonActionCommand { get; }
    public DelegateCommand PickWaveSignalCommand { get; }
    public DelegateCommand PickKeyCombinationCommand { get; }
    public DelegateCommand PickSupportedKeysCommand { get; }
    public DelegateCommand AddStateCommand { get; }
    public DelegateCommand SetDefaultCommand { get; }
    public DelegateCommand AddRuleCommand { get; }
    public DelegateCommand AddRecipeCommand { get; }
    public DelegateCommand RemoveEntryCommand { get; }

    private void AddField(string key, string label, string? value, string hint = "",
        IReadOnlyList<string>? options = null, bool readOnly = false)
    {
        var f = new ConfigurationField(key, label, value ?? "", hint, options, readOnly);
        f.PropertyChanged += FieldChanged;
        Fields.Add(f);
    }
    private void FieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConfigurationField.Value)) return;
        _fieldEdited = true;
        RaisePropertyChanged(nameof(IsDirty));
    }
    private string F(string key) => Fields.First(x => x.Key == key).Value.Trim();
    private static string Format(double? n) => n?.ToString("R", CultureInfo.InvariantCulture) ?? "";
    private static double? Number(string s) => string.IsNullOrWhiteSpace(s)
        ? null : double.Parse(s, CultureInfo.InvariantCulture);
    private static uint ParseCan(string value) =>
        uint.Parse(value.Replace("0x", "", StringComparison.OrdinalIgnoreCase),
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static uint? ParseOptionalCan(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseCan(value);
    private static bool Yes(string input)
    {
        if (input == "是") return true;
        if (input == "否") return false;
        return bool.Parse(input);
    }
    private static readonly IReadOnlyList<string> YesNo = ["否", "是"];
    private static readonly IReadOnlyList<string> Interactions =
        Enum.GetNames<ControlInteraction>();

    private void PopulateFields()
    {
        foreach (var field in Fields) field.PropertyChanged -= FieldChanged;
        Fields.Clear();
        _fieldEdited = false;
        var key = _selectedNode?.Key ?? "profile";
        if (_working is EcuControlProfile ctrl)
        {
            if (key == "profile")
            {
                AddField("ecu", "ECU 名称", ctrl.EcuName);
                AddField("board", "按键模拟板型号", ctrl.HardwareModel,
                    "修改名称不会改变实际 USB/CAN 硬件。");
                AddField("boardId", "硬件配置标识（只读）", ctrl.HardwareProfileId.ToString(),
                    "用于标识按键模拟板的配置版本。", readOnly: true);
                AddField("supportedKeys", "已确认可用的电阻开关组合",
                    ctrl.KeyHardwarePolicy is null ? "" :
                        "0x" + ctrl.KeyHardwarePolicy.SupportedMask.ToString("X5"),
                    "点击上方“选择硬件可用 Key”勾选硬件资源；未经确认不能进行真实按键操作。",
                    readOnly: true);
                AddField("keyConflicts", "禁止同时闭合的 Key 组合",
                    ctrl.KeyHardwarePolicy is null ? "" :
                        string.Join("; ", ctrl.KeyHardwarePolicy.ForbiddenPairs.Select(
                            pair => "0x" + pair.A.ToString("X") + "+0x" + pair.B.ToString("X"))),
                    "每项格式 0x1+0x2；多个以分号分隔。空值表示没有额外禁止组合，需工程师实际确认。");
            }
            else if (key.StartsWith("controller|", StringComparison.Ordinal))
            {
                var c = ctrl.Controllers.Single(x => x.Id == SelectedControllerId);
                AddField("name", "控制器名称", c.Name);
                AddField("presentation", "操作展示方式", c.Presentation);
                AddField("default", "默认状态", c.States.Single(x => x.Id == c.DefaultStateId).Name,
                    "通过“设为默认”按钮切换。", readOnly: true);
            }
            else if (key.StartsWith("state|", StringComparison.Ordinal))
            {
                var c = ctrl.Controllers.Single(x => x.Id == SelectedControllerId);
                var state = c.States.Single(x => x.Id == SelectedStateId);
                AddField("name", "状态名称", state.Name);
                AddField("mask", "模拟此状态的电阻开关组合",
                    "0x" + state.KeyMask.ToString("X5"),
                    "点击上方“选择电阻组合”，勾选用于形成该状态模拟电压的 Key。"
                    + " 所填目标电压仍需现场测量验证。", readOnly: true);
                AddField("interaction", "交互方式", state.Interaction.ToString(), "", Interactions);
                AddField("return", "弹回目标状态 ID", state.ReturnStateId?.ToString(),
                    "仅 SpringReturn 状态需要目标 State ID。");
                AddField("group", "互斥组", state.ExclusiveGroup);
                AddField("pressDelay", "按下前固定延迟（ms）",
                    state.ActionTiming.DelayBeforePressMs.ToString(CultureInfo.InvariantCulture),
                    "从步骤请求到开始施加电阻组合的固定延迟。");
                AddField("pressHold", "短按默认保持时间（ms）",
                    state.ActionTiming.DefaultPressDurationMs.ToString(CultureInfo.InvariantCulture),
                    "0 表示未指定；短按步骤必须明确设置正数。持续保持由测试步骤选择。");
                AddField("releaseDelay", "释放后固定间隔（ms）",
                    state.ActionTiming.DelayAfterReleaseMs.ToString(CultureInfo.InvariantCulture),
                    "只决定动作间隔，不表示 ECU 已产生响应。");
                for (int ch = 1; ch <= 5; ch++)
                {
                    var window = state.Windows.FirstOrDefault(x => x.Channel == ch);
                    AddField("target" + ch, "CH" + ch + " 目标电压 V", Format(window?.TargetVolts));
                    AddField("min" + ch, "CH" + ch + " 识别下限 V", Format(window?.MinimumVolts));
                    AddField("max" + ch, "CH" + ch + " 识别上限 V", Format(window?.MaximumVolts));
                }
            }
        }
        else if (_working is WaveSettingsDocument wave)
        {
            AddField("name", "配置名称", wave.Name);
            AddField("dbc", "DBC 工程资源", AssetNameFor(wave.DbcAssetId),
                "使用上方“从 DBC 选择信号”按钮自动绑定资源、报文及信号。", readOnly: true);
            AddField("role", "CAN 通道", wave.Bus.ToString(), "CAN1 为 DUT，CAN0 为按键板",
                Enum.GetNames<BusRole>());
            AddField("id", "报文 ID（HEX）", "0x" + wave.CanId.ToString("X"),
                "由所选 DBC 信号确定，不手工输入。", readOnly: true);
            AddField("signal", "DBC 信号名称", wave.SignalName, readOnly: true);
            AddField("unit", "信号单位", wave.Unit, readOnly: true);
            AddField("reference", "关联波形证据（非参考波形）", AssetNameFor(wave.EvidenceAssetId),
                "可选。用于关联已验证的波形证据；正式参考波形需另行采集并明确保存。", AssetNames(ProjectWorkspaceService.WaveKind));
            AddField("abs", "绝对容差", Format(wave.Comparison.AbsoluteTolerance));
            AddField("rel", "相对容差", Format(wave.Comparison.RelativeTolerance));
            AddField("coverage", "最小覆盖率（0~1）", Format(wave.Comparison.MinimumCoverage));
            AddField("ratio", "最小合格比例（0~1）", Format(wave.Comparison.MinimumPassRatio));
            AddField("shift", "最大时间平移 秒", Format(wave.Comparison.MaximumShiftSeconds));
            AddField("gap", "最大间隙系数", Format(wave.Comparison.MaximumGapFactor));
            AddField("outside", "最大超差持续 秒", Format(wave.Comparison.MaximumOutOfToleranceSeconds));
            AddField("duration", "最小有效持续 秒", Format(wave.Comparison.MinimumCoveredDurationSeconds));
            AddField("sync", "要求硬件同步时间", wave.Comparison.RequireSynchronizedHardwareTime ? "是" : "否", "", YesNo);
        }
        else if (_working is FlashSettingsDocument flash)
        {
            AddField("name", "配置名称", flash.Name);
            AddField("target", "ECU 型号 / 目标", flash.Target);
            AddField("identity", "预期 ECU 识别值", flash.ExpectedEcuIdentity);
            AddField("image", "固件资源", AssetNameFor(flash.FirmwareAssetId),
                "只能引用工程中已有的 HEX / S-record", AssetNames(ProjectWorkspaceService.FirmwareKind));
            AddField("extended", "扩展 CAN ID", flash.ExtendedCanId ? "是" : "否", "", YesNo);
            AddField("request", "UDS 请求 CAN ID（HEX）", flash.RequestCanId.HasValue
                ? "0x" + flash.RequestCanId.Value.ToString("X") : "");
            AddField("response", "UDS 响应 CAN ID（HEX）", flash.ResponseCanId.HasValue
                ? "0x" + flash.ResponseCanId.Value.ToString("X") : "");
            AddField("p2", "P2 超时 ms", flash.P2Milliseconds.ToString(CultureInfo.InvariantCulture));
            AddField("p2star", "P2* 超时 ms", flash.P2StarMilliseconds.ToString(CultureInfo.InvariantCulture));
            AddField("session", "诊断会话", flash.Session);
            AddField("security", "SecurityAccess 级别", flash.SecurityLevel);
            AddField("keydll", "Seed/Key 算法或 DLL", flash.SeedKeyProvider,
                "此字段只是配置，不会动态加载未审查的 DLL。");
            AddField("erase", "擦除 Routine ID", flash.EraseRoutine);
            AddField("verify", "校验 Routine ID", flash.VerifyRoutine);
            AddField("memory", "允许写入的地址范围", flash.MemoryRanges,
                "明确目标 Flash 映射之前不可实际烧录。");
        }
        else if (_working is ScanMatchSettingsDocument scan)
        {
            if (key == "profile")
            {
                AddField("name", "配置名称", scan.Name);
                AddField("published", "发布规则与配方", scan.Published ? "是" : "否", "",
                    YesNo);
            }
            else if (key.StartsWith("rule|", StringComparison.Ordinal))
            {
                var r = scan.Rules[ParseIndex(key)];
                AddField("id", "规则编号", r.RuleId);
                AddField("regex", "扫码完整匹配正则表达式", r.Pattern,
                    "使用命名捕获组，例如 (?<model>...)、(?<serial>...)。");
                AddField("keys", "身份字段（英文逗号分隔）", r.IdentityFields,
                    "例如 serial 或 model,serial，字段必须属于命名捕获组。");
                AddField("published", "发布此规则", r.Published ? "是" : "否", "", YesNo);
            }
            else if (key.StartsWith("recipe|", StringComparison.Ordinal))
            {
                var r = scan.Recipes[ParseIndex(key)];
                AddField("id", "配方编号", r.RecipeId);
                AddField("model", "要求车型 / model", r.Model);
                AddField("customer", "要求客户 / customer", r.Customer);
                AddField("test", "关联测试程序", AssetNameFor(r.TestProgramAssetId),
                    "仅允许已保存的 TEST 资源", AssetNames(ProjectWorkspaceService.TestProgramKind));
                AddField("dbc", "关联 DBC", AssetNameFor(r.DbcAssetId),
                    "可选。引用当前工程 DBC 版本", AssetNames(ProjectWorkspaceService.DbcKind));
                AddField("flash", "关联固件", AssetNameFor(r.FirmwareAssetId),
                    "可选。需要烧录时才要求", AssetNames(ProjectWorkspaceService.FirmwareKind));
                AddField("requiresFlash", "要求烧录", r.RequiresFlash ? "是" : "否", "", YesNo);
                AddField("published", "发布此配方", r.Published ? "是" : "否", "", YesNo);
            }
        }
        RaisePropertyChanged(nameof(IsDirty));
    }

    private IReadOnlyList<string> AssetNames(string kind) =>
        new[] { "" }.Concat(_workspace.Current?.Definition.Assets
            .Where(x => x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name) ?? []).ToArray();
    private string AssetNameFor(Guid? id) =>
        _workspace.Current?.Definition.Assets.FirstOrDefault(x => x.Id.Value == id)?.Name ?? "";
    private (Guid? Id, int? Revision, string? Sha256) ResolvePinnedAsset(string name, string kind)
    {
        var id = ResolveAsset(name, kind);
        if (id is null) return (null, null, null);
        var asset = _workspace.Current!.Definition.Assets.Single(x => x.Id.Value == id.Value);
        return (asset.Id.Value, asset.Revision, asset.Sha256);
    }

    private Guid? ResolveAsset(string name, string kind)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var matches = _workspace.Current?.Definition.Assets.Where(x =>
            x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x.Id.Value.ToString("D"), name, StringComparison.OrdinalIgnoreCase))).ToArray() ?? [];
        if (matches.Length != 1)
            throw new ProjectDataException("CONFIG_ASSET_REFERENCE",
                "必须选择工程中唯一存在的 " + kind + " 资源：" + name);
        return matches[0].Id.Value;
    }

    private static HardwareKeyPolicy? ParseHardwarePolicy(
        string supportedText, string conflictText, Guid hardwareId)
    {
        if (string.IsNullOrWhiteSpace(supportedText))
        {
            if (!string.IsNullOrWhiteSpace(conflictText))
                throw new ProjectDataException("CTRL_HARDWARE",
                    "必须先设置可用硬件 Key 掩码，才能设置互斥组合。");
            return null;
        }
        var forbidden = ImmutableArray.CreateBuilder<IncompatibleKeyPair>();
        foreach (var pair in conflictText.Split(';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var keys = pair.Split('+', StringSplitOptions.TrimEntries);
            if (keys.Length != 2)
                throw new ProjectDataException("CTRL_KEY_CONFLICT",
                    "禁止组合格式应为 0x1+0x2; 0x4+0x8。");
            forbidden.Add(new IncompatibleKeyPair(ParseCan(keys[0]), ParseCan(keys[1])));
        }
        return new HardwareKeyPolicy(hardwareId, ParseCan(supportedText),
            forbidden.ToImmutable()).Validate();
    }

    private static int ParseIndex(string key) =>
        int.Parse(key.Split('|')[1], CultureInfo.InvariantCulture);

    public bool ApplyFields()
    {
        if (!_fieldEdited || _selectedNode is null) return true;
        try
        {
            var key = _selectedNode.Key;
            object next = _working;
            if (_working is EcuControlProfile profile)
            {
                if (key == "profile")
                    next = profile with { EcuName = F("ecu"), HardwareModel = F("board"),
                        KeyHardwarePolicy = ParseHardwarePolicy(
                            F("supportedKeys"), F("keyConflicts"), profile.HardwareProfileId),
                        Revision = checked(profile.Revision + 1) };
                else if (key.StartsWith("controller|", StringComparison.Ordinal))
                {
                    var index = Array.FindIndex(profile.Controllers.ToArray(), x => x.Id == SelectedControllerId);
                    var controller = profile.Controllers[index];
                    next = profile with
                    {
                        Controllers = profile.Controllers.SetItem(index,
                            controller with { Name = F("name"), Presentation = F("presentation") }),
                        Revision = checked(profile.Revision + 1)
                    };
                }
                else if (key.StartsWith("state|", StringComparison.Ordinal))
                {
                    var index = Array.FindIndex(profile.Controllers.ToArray(), x => x.Id == SelectedControllerId);
                    var controller = profile.Controllers[index];
                    var stateIndex = Array.FindIndex(controller.States.ToArray(), x => x.Id == SelectedStateId);
                    var state = controller.States[stateIndex];
                    var mask = ParseCan(F("mask"));
                    var windows = ImmutableArray.CreateBuilder<VoltageWindow>();
                    for (int ch = 1; ch <= 5; ch++)
                    {
                        var target = Number(F("target" + ch));
                        var min = Number(F("min" + ch));
                        var max = Number(F("max" + ch));
                        if (target.HasValue || min.HasValue || max.HasValue)
                            windows.Add(new VoltageWindow(ch, target, min, max).Validate());
                    }
                    Guid? returnId = string.IsNullOrEmpty(F("return"))
                        ? null : Guid.Parse(F("return"));
                    var revisedState = state with
                    {
                        Name = F("name"), KeyMask = mask,
                        Interaction = Enum.Parse<ControlInteraction>(F("interaction"), true),
                        ReturnStateId = returnId, ExclusiveGroup = F("group"),
                        ActionTiming = new ButtonActionTiming(
                            int.Parse(F("pressDelay"), CultureInfo.InvariantCulture),
                            int.Parse(F("pressHold"), CultureInfo.InvariantCulture),
                            int.Parse(F("releaseDelay"), CultureInfo.InvariantCulture)).Validate(),
                        Windows = windows.ToImmutable()
                    };
                    next = profile with
                    {
                        Controllers = profile.Controllers.SetItem(index,
                            controller with { States = controller.States.SetItem(stateIndex, revisedState) }),
                        Revision = checked(profile.Revision + 1)
                    };
                }
            }
            else if (_working is WaveSettingsDocument wave)
            {
                var dbc = ResolvePinnedAsset(F("dbc"), ProjectWorkspaceService.DbcKind);
                var reference = ResolvePinnedAsset(F("reference"), ProjectWorkspaceService.WaveKind);
                next = wave with
                {
                    Name = F("name"), DbcAssetId = dbc.Id,
                    DbcAssetRevision = dbc.Revision, DbcAssetFingerprint = dbc.Sha256,
                    EvidenceAssetRevision = reference.Revision,
                    EvidenceAssetFingerprint = reference.Sha256,
                    Bus = Enum.Parse<BusRole>(F("role"), true),
                    CanId = ParseCan(F("id")), SignalName = F("signal"), Unit = F("unit"),
                    EvidenceAssetId = reference.Id,
                    Comparison = new WaveComparisonConfig(
                        Number(F("abs")) ?? 0, Number(F("rel")) ?? 0, Number(F("coverage")) ?? 0,
                        Number(F("ratio")) ?? 0, Number(F("shift")) ?? 0, Number(F("gap")) ?? 0,
                        Number(F("outside")) ?? 0, Number(F("duration")) ?? 0)
                    { RequireSynchronizedHardwareTime = Yes(F("sync")) }
                };
            }
            else if (_working is FlashSettingsDocument flash)
            {
                var firmware = ResolvePinnedAsset(F("image"), ProjectWorkspaceService.FirmwareKind);
                next = flash with
                {
                    Name = F("name"), Target = F("target"), ExpectedEcuIdentity = F("identity"),
                    FirmwareAssetId = firmware.Id,
                    FirmwareRevision = firmware.Revision, FirmwareFingerprint = firmware.Sha256,
                    ExtendedCanId = Yes(F("extended")),
                    RequestCanId = ParseOptionalCan(F("request")),
                    ResponseCanId = ParseOptionalCan(F("response")),
                    P2Milliseconds = int.Parse(F("p2"), CultureInfo.InvariantCulture),
                    P2StarMilliseconds = int.Parse(F("p2star"), CultureInfo.InvariantCulture),
                    Session = F("session"), SecurityLevel = F("security"),
                    SeedKeyProvider = F("keydll"), EraseRoutine = F("erase"),
                    VerifyRoutine = F("verify"), MemoryRanges = F("memory"),
                    ApprovedByOperator = false
                };
            }
            else if (_working is ScanMatchSettingsDocument scan)
            {
                if (key == "profile") next = scan with
                { Name = F("name"), Published = Yes(F("published")) };
                else if (key.StartsWith("rule|", StringComparison.Ordinal))
                {
                    var rules = scan.Rules.ToList();
                    rules[ParseIndex(key)] = new ScanRuleDraft(F("id"), F("regex"),
                        F("keys"), Yes(F("published")));
                    next = scan with { Rules = rules };
                }
                else if (key.StartsWith("recipe|", StringComparison.Ordinal))
                {
                    var recipes = scan.Recipes.ToList();
                    var test = ResolvePinnedAsset(F("test"), ProjectWorkspaceService.TestProgramKind);
                    var firmware = ResolvePinnedAsset(F("flash"), ProjectWorkspaceService.FirmwareKind);
                    var dbc = ResolvePinnedAsset(F("dbc"), ProjectWorkspaceService.DbcKind);
                    recipes[ParseIndex(key)] = new ScanRecipeDraft(F("id"), F("model"),
                        F("customer"), test.Id, firmware.Id, dbc.Id,
                        Yes(F("requiresFlash")), Yes(F("published")),
                        test.Revision, test.Sha256,
                        firmware.Revision, firmware.Sha256, dbc.Revision, dbc.Sha256);
                    next = scan with { Recipes = recipes };
                }
            }
            ProjectConfigurationCodec.Validate(_session.Kind, next);
            _working = next;
            _fieldEdited = false;
            StatusMessage = "参数已应用到当前编辑草稿，点击保存才会写入工程。";
            RaisePropertyChanged(nameof(IsDirty));
            return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException
                                      or ProjectDataException)
        {
            StatusMessage = "参数校验未通过：" + error.Message;
            return false;
        }
    }

    private void RebuildNodes(string? preferredKey = null)
    {
        var items = new List<ConfigurationNode> { new("profile", "配置总览") };
        if (_working is EcuControlProfile profile)
            foreach (var controller in profile.Controllers.OrderBy(x => x.Order))
            {
                items.Add(new("controller|" + controller.Id, controller.Name));
                foreach (var state in controller.States.OrderBy(x => x.Order))
                    items.Add(new("state|" + controller.Id + "|" + state.Id,
                        "    " + state.Name + (state.Id == controller.DefaultStateId ? " · 默认" : "")));
            }
        if (_working is ScanMatchSettingsDocument scan)
        {
            for (int i = 0; i < scan.Rules.Count; i++)
                items.Add(new("rule|" + i, "规则 · " + scan.Rules[i].RuleId));
            for (int i = 0; i < scan.Recipes.Count; i++)
                items.Add(new("recipe|" + i, "配方 · " + scan.Recipes[i].RecipeId));
        }
        var key = preferredKey ?? _selectedNode?.Key ?? "profile";
        _fieldEdited = false;
        _selectedNode = null;
        Nodes.Clear();
        foreach (var entry in items) Nodes.Add(entry);
        SelectedNode = Nodes.FirstOrDefault(x => x.Key == key) ?? Nodes[0];
    }

    private void PickKeys(string fieldKey)
    {
        if (!IsController || IsBusy || !ApplyFields()) return;
        try
        {
            var field = Fields.Single(f => f.Key == fieldKey);
            var initial = string.IsNullOrWhiteSpace(field.Value)
                ? (fieldKey == "supportedKeys" ? 0xFFFFFu : 0u)
                : ParseCan(field.Value);
            var dialog = new KeyMaskPickerDialog(initial,
                fieldKey == "supportedKeys" ? "选择硬件已确认可用的 Key" : "选择模拟状态的电阻组合");
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current.MainWindow;
            if (owner is { IsLoaded: true }) dialog.Owner = owner;
            if (dialog.ShowDialog() != true) return;
            field.Value = "0x" + dialog.SelectedMask.ToString("X5");
            StatusMessage = fieldKey == "supportedKeys"
                ? "已选择硬件可用 Key；请核对真实硬件限制，再应用并保存。"
                : "已配置此功能状态的 Key 组合；需核对模拟输出电压及识别区间。";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or
                                      ProjectDataException or FormatException)
        {
            StatusMessage = "选择 Key 组合失败：" + error.Message;
        }
    }

    private void AddButtonAction()
    {
        if (!ApplyFields()) return;
        try
        {
            var edit = new ControllerEditSession((EcuControlProfile)_working);
            var name = "模拟按键" + (edit.Current.Controllers.Length + 1);
            edit.AddController(name, "Buttons");
            var controller = edit.Current.Controllers[^1];
            edit.EditState(controller.Id, controller.States[0] with
            { Name = "松开", Interaction = ControlInteraction.Latched });
            edit.AddState(controller.Id, "按下", 0);
            _working = edit.Current;
            var pressed = edit.Current.Controllers[^1].States[^1];
            RebuildNodes("state|" + controller.Id + "|" + pressed.Id);
            StatusMessage = "已创建“" + name +
                "”的松开/按下状态。请配置按下状态的 Key 组合、电压窗口和固定延迟，再保存工程。";
            RaisePropertyChanged(nameof(IsDirty));
        }
        catch (ProjectDataException error) { StatusMessage = error.Message; }
    }

    private async Task PickWaveSignalAsync()
    {
        if (!IsWave || IsBusy) return;
        IsBusy = true;
        try
        {
            var all = await TestTargetCatalog.LoadAsync(_workspace);
            var available = all.Where(x => x.Mapping.Kind == AutoTest.Testing.Authoring.TestTargetKind.Signal)
                .ToArray();
            if (available.Length == 0)
            {
                StatusMessage = "工程还没有可选的 DBC 信号。请先导入有效 DBC。";
                return;
            }
            var dialog = new WaveSignalPickerDialog(available);
            var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current.MainWindow;
            if (owner is { IsLoaded: true }) dialog.Owner = owner;
            if (dialog.ShowDialog() != true || dialog.Selected is not { } mapping) return;
            var asset = _workspace.Current?.Definition.Assets.SingleOrDefault(a => a.Id == mapping.Asset)
                ?? throw new ProjectDataException("WAVE_DBC_MISSING", "DBC 工程资源已切换。");
            void Assign(string key, string value) => Fields.Single(f => f.Key == key).Value = value;
            Assign("dbc", asset.Name);
            Assign("role", mapping.Role.ToString());
            Assign("id", "0x" + mapping.CanId.ToString("X"));
            Assign("signal", mapping.SignalName);
            Assign("unit", mapping.Unit);
            StatusMessage = "已选择 DBC 信号：" + mapping.Name +
                "。请配置观察和分析参数，点击“应用参数”后保存。";
        }
        catch (Exception error) when (error is ProjectDataException or IOException or
                                      UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            StatusMessage = "选择 DBC 信号失败：" + error.Message;
            _dialogs.ShowError(StatusMessage);
        }
        finally { IsBusy = false; }
    }

    private void AddController()
    {
        if (!ApplyFields()) return;
        try
        {
            var edit = new ControllerEditSession((EcuControlProfile)_working);
            edit.AddController("新控制器" + (edit.Current.Controllers.Length + 1));
            _working = edit.Current;
            RebuildNodes("controller|" + edit.Current.Controllers[^1].Id);
            RaisePropertyChanged(nameof(IsDirty));
        }
        catch (ProjectDataException error) { StatusMessage = error.Message; }
    }
    private void AddState()
    {
        if (!ApplyFields() || SelectedControllerId is not Guid controllerId) return;
        try
        {
            var edit = new ControllerEditSession((EcuControlProfile)_working);
            var controller = edit.Current.Controllers.Single(x => x.Id == controllerId);
            edit.AddState(controllerId, "新状态" + (controller.States.Length + 1), 0);
            _working = edit.Current;
            var next = edit.Current.Controllers.Single(x => x.Id == controllerId).States[^1];
            RebuildNodes("state|" + controllerId + "|" + next.Id);
            RaisePropertyChanged(nameof(IsDirty));
        }
        catch (ProjectDataException error) { StatusMessage = error.Message; }
    }
    private void SetDefault()
    {
        if (!ApplyFields() || SelectedControllerId is not Guid controllerId ||
            SelectedStateId is not Guid stateId) return;
        try
        {
            var edit = new ControllerEditSession((EcuControlProfile)_working);
            edit.SetDefault(controllerId, stateId);
            _working = edit.Current;
            RebuildNodes("state|" + controllerId + "|" + stateId);
            RaisePropertyChanged(nameof(IsDirty));
        }
        catch (ProjectDataException error) { StatusMessage = error.Message; }
    }
    private void AddRule()
    {
        if (!ApplyFields()) return;
        var scan = (ScanMatchSettingsDocument)_working;
        if (scan.Published) { StatusMessage = "请先取消总发布状态，再添加未配置规则。"; return; }
        var ruleId = "Rule" + Guid.NewGuid().ToString("N")[..6];
        _working = scan with { Rules = scan.Rules.Append(new ScanRuleDraft(ruleId, "", "", false)).ToList() };
        RebuildNodes("rule|" + scan.Rules.Count);
        RaisePropertyChanged(nameof(IsDirty));
    }
    private void AddRecipe()
    {
        if (!ApplyFields()) return;
        var scan = (ScanMatchSettingsDocument)_working;
        if (scan.Published) { StatusMessage = "请先取消总发布状态，再添加未配置配方。"; return; }
        var id = "Recipe" + Guid.NewGuid().ToString("N")[..6];
        _working = scan with { Recipes = scan.Recipes.Append(new ScanRecipeDraft(id, "", "", null,
            null, null, false, false)).ToList() };
        RebuildNodes("recipe|" + scan.Recipes.Count);
        RaisePropertyChanged(nameof(IsDirty));
    }
    private void RemoveSelected()
    {
        if (!ApplyFields() || _selectedNode is null || _selectedNode.Key == "profile") return;
        try
        {
            var key = _selectedNode.Key;
            if (_working is EcuControlProfile controller && SelectedControllerId is Guid controllerId)
            {
                var edit = new ControllerEditSession(controller);
                if (key.StartsWith("controller|", StringComparison.Ordinal))
                    edit.RemoveController(controllerId);
                else if (SelectedStateId is Guid stateId)
                    edit.RemoveState(controllerId, stateId);
                _working = edit.Current;
            }
            else if (_working is ScanMatchSettingsDocument scan)
            {
                if (key.StartsWith("rule|", StringComparison.Ordinal))
                {
                    var list = scan.Rules.ToList(); list.RemoveAt(ParseIndex(key));
                    _working = scan with { Rules = list };
                }
                else if (key.StartsWith("recipe|", StringComparison.Ordinal))
                {
                    var list = scan.Recipes.ToList(); list.RemoveAt(ParseIndex(key));
                    _working = scan with { Recipes = list };
                }
                ProjectConfigurationCodec.Validate(_session.Kind, _working);
            }
            RebuildNodes("profile");
            RaisePropertyChanged(nameof(IsDirty));
        }
        catch (Exception error) when (error is ProjectDataException or InvalidOperationException)
        { StatusMessage = "无法删除：" + error.Message; }
    }

    public bool ConfirmClose() =>
        !IsBusy && (!IsDirty || MessageBox.Show(Application.Current.MainWindow,
            "工程配置还有未保存的修改。确定放弃并切换吗？", "未保存配置",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes);

    private async Task SaveAsync()
    {
        if (!ApplyFields() || IsBusy) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.SaveConfigurationAsync(_session, _working);
            _working = _session.Document;
            RebuildNodes();
            RaisePropertyChanged(nameof(Revision));
            RaisePropertyChanged(nameof(IsDirty));
            StatusMessage = "配置已保存并重新读取；工程资源修订 " + Revision;
        }
        catch (Exception error) when (error is ProjectDataException or IOException or
                                    UnauthorizedAccessException or ArgumentException or
                                    InvalidDataException or FormatException)
        {
            StatusMessage = "保存失败：" + error.Message;
            _dialogs.ShowError(StatusMessage);
        }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync()
    {
        if (IsBusy || !ConfirmClose()) return;
        IsBusy = true;
        try
        {
            _session = await _workspace.OpenConfigurationAsync(_session.AssetId);
            _working = _session.Document;
            RebuildNodes("profile");
            RaisePropertyChanged(nameof(Revision));
            RaisePropertyChanged(nameof(IsDirty));
            StatusMessage = "已从磁盘重新加载。";
        }
        catch (Exception error) when (error is ProjectDataException or IOException or
                                    UnauthorizedAccessException or ArgumentException)
        { StatusMessage = "重新加载失败：" + error.Message; }
        finally { IsBusy = false; }
    }

    private void RefreshCommands()
    {
        SaveCommand.RaiseCanExecuteChanged();
        ReloadCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        AddControllerCommand.RaiseCanExecuteChanged();
        AddButtonActionCommand.RaiseCanExecuteChanged();
        PickWaveSignalCommand.RaiseCanExecuteChanged();
        PickKeyCombinationCommand.RaiseCanExecuteChanged();
        PickSupportedKeysCommand.RaiseCanExecuteChanged();
        AddStateCommand.RaiseCanExecuteChanged();
        SetDefaultCommand.RaiseCanExecuteChanged();
        AddRuleCommand.RaiseCanExecuteChanged();
        AddRecipeCommand.RaiseCanExecuteChanged();
        RemoveEntryCommand.RaiseCanExecuteChanged();
    }
}
