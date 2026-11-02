using Application.Contracts.Dtos.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Dicts;
/// <summary>字典类型分页查询请求（GET /api/system/dict-type/page）。</summary>
public class DictTypePageRequest : AggregatePagedRequest
{
    /// <summary>关键字：模糊匹配字典名称 / 字典类型。</summary>
    public string? Keyword { get; set; }
}

/// <summary>字典类型列表行（分页数据项）。</summary>
public class DictTypePagedResponse : AggregatePagedResponse
{
    /// <summary>字典名称。</summary>
    public string DictName { get; set; } = string.Empty;

    /// <summary>字典类型（唯一）。</summary>
    public string DictType { get; set; } = string.Empty;

}

/// <summary>字典类型写入请求（POST / PUT 共用载荷；对齐 SysDictType 聚合；DictType 创建后不可变更）。</summary>
public class DictTypeInputRequest : AggregateInputRequest
{
    /// <summary>字典名称。</summary>
    public string DictName { get; set; } = string.Empty;

    /// <summary>字典类型（唯一）。</summary>
    public string DictType { get; set; } = string.Empty;

    /// <summary>备注。</summary>
    public string? Description { get; set; }
}

/// <summary>字典数据分页查询请求（GET /api/system/dict-data/page）。</summary>
public class DictDataPageRequest : AggregatePagedRequest
{
    /// <summary>所属字典类型精确过滤。</summary>
    public string? DictType { get; set; }

    /// <summary>关键字：模糊匹配标签 / 键值。</summary>
    public string? Keyword { get; set; }
}

/// <summary>字典数据列表行（分页数据项）。</summary>
public class DictDataPagedResponse : AggregatePagedResponse
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; }

    /// <summary>所属字典类型。</summary>
    public string DictType { get; set; } = string.Empty;

    /// <summary>字典标签（显示名称）。</summary>
    public string DictLabel { get; set; } = string.Empty;

    /// <summary>字典键值（存储值）。</summary>
    public string DictValue { get; set; } = string.Empty;

    /// <summary>标签样式（见 <see cref="Domain.Shared.Enums.TagType"/>）。</summary>
    public TagType TagType { get; set; }

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }
}

/// <summary>字典数据简项（GET /api/system/dict-data/simple/{dictType}，供前端下拉；Id 由 <see cref="AggregateDropdownResponse"/> 承载）。</summary>
public class DictDataDropdownResponse : AggregateDropdownResponse
{
    /// <summary>字典标签（显示名称）。</summary>
    public string DictLabel { get; set; } = string.Empty;

    /// <summary>字典键值（存储值）。</summary>
    public string DictValue { get; set; } = string.Empty;

    /// <summary>标签样式（见 <see cref="Domain.Shared.Enums.TagType"/>）。</summary>
    public TagType TagType { get; set; }
}

/// <summary>字典数据写入请求（POST / PUT 共用载荷；镜像 SysDictData 聚合的启停 / 备注与业务字段）。</summary>
public class DictDataInputRequest : AggregateInputRequest
{
    /// <summary>状态：启用（1），停用（0）。</summary>
    public CommonStatus Status { get; set; } = CommonStatus.Enabled;

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>所属字典类型（必须已存在）。</summary>
    public string DictType { get; set; } = string.Empty;

    /// <summary>字典标签（同字典类型下唯一）。</summary>
    public string DictLabel { get; set; } = string.Empty;

    /// <summary>字典键值（同字典类型下唯一）。</summary>
    public string DictValue { get; set; } = string.Empty;

    /// <summary>标签样式（见 <see cref="Domain.Shared.Enums.TagType"/>）。</summary>
    public TagType TagType { get; set; } = TagType.Primary;

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }
}

/// <summary>按字典类型查询字典数据请求（载荷即字典类型编码）。</summary>
public class DictDataByTypeRequest(string DictType) : AggregateListRequest
{
    /// <summary>字典类型编码。</summary>
    public string DictType { get; } = DictType;
}
