namespace Infrastructure.Shared.Options;

/// <summary>多租户配置项（绑定配置节 MultiTenancy）。</summary>
public sealed class MultiTenancyOption : OptionBase
{
    /// <summary>是否启用多租户数据隔离（关闭时租户相关校验全部放行，保持原平台行为）。</summary>
    public bool Enabled { get; set; }

    /// <inheritdoc />
    public override void Validate()
    {
    }
}
