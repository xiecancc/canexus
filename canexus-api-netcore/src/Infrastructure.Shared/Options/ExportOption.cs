namespace Infrastructure.Shared.Options;

/// <summary>
/// Excel 导出阈值（绑定配置节 "Export"，EX-05 阈值 Option 化）。
/// <para>原为 <c>ExcelExportSupport.MaxRows</c> 的编译期常量 10000，改由配置注入后
/// 可按内存规格 / 业务需要调整单次导出行数上限；默认值与历史行为一致。
/// 应用层 <c>ExcelExportSupport</c> 仍保留同名常量作为缺省回退值。</para>
/// </summary>
public sealed class ExportOption : OptionBase
{
    /// <summary>单次导出允许的最大行数（超出部分截断并在文件名如实标注已导出行数），默认 10000 行。</summary>
    public int MaxRows { get; set; } = 10000;

    /// <inheritdoc />
    public override void Validate()
    {
        EnsureInRange(MaxRows, 1, 200_000, nameof(MaxRows),
            "Export:MaxRows 必须在 1-200000 行之间（防止单次导出耗尽内存）");
    }
}
