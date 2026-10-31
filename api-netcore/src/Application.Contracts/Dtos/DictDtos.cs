
using Application.Contracts.Common;
using Domain.Shared.Enums;
using Domain.Shared.Paging;

namespace Application.Contracts.Dicts;
/// <summary>字典类型分页查询请求（GET /api/system/dict-type/page）。</summary>
public class DictTypePageRequest : PagedRequest
{
    /// <summary>关键字：模糊匹配字典名称 / 字典类型。</summary>
    public string? Keyword { get; set; }
}

/// <summary>字典类型列表行（分页数据项）。</summary>
public class DictTypeListVO : EntityVO
{
    /// <summary>字典名称。</summary>
    public string DictName { get; set; } = string.Empty;

    /// <summary>字典类型（唯一）。</summary>
    public string DictType { get; set; } = string.Empty;

    /// <summary>备注。</summary>
    public string? Description { get; set; }
}

/// <summary>字典类型写入请求基类：创建 / 更新共同字段（对齐 SysDictType 聚合；DictType 创建后不可变更）。</summary>
public abstract class DictTypeWriteRequest
{
    /// <summary>字典名称。</summary>
    public string DictName { get; set; } = string.Empty;

    /// <summary>字典类型（唯一）。</summary>
    public string DictType { get; set; } = string.Empty;

    /// <summary>备注。</summary>
    public string? Description { get; set; }
}

/// <summary>创建字典类型请求（POST /api/system/dict-type）。</summary>
public class DictTypeCreateRequest : DictTypeWriteRequest
{
}

/// <summary>更新字典类型请求（PUT /api/system/dict-type/{id}；DictType 不可变更）。</summary>
public class DictTypeUpdateRequest : DictTypeWriteRequest
{
}

/// <summary>字典数据分页查询请求（GET /api/system/dict-data/page）。</summary>
public class DictDataPageRequest : PagedRequest
{
    /// <summary>所属字典类型精确过滤。</summary>
    public string? DictType { get; set; }

    /// <summary>关键字：模糊匹配标签 / 键值。</summary>
    public string? Keyword { get; set; }
}

/// <summary>字典数据列表行（分页数据项）。</summary>
public class DictDataListVO : StatusVO
{
    /// <summary>所属字典类型。</summary>
    public string DictType { get; set; } = string.Empty;

    /// <summary>字典标签（显示名称）。</summary>
    public string DictLabel { get; set; } = string.Empty;

    /// <summary>字典键值（存储值）。</summary>
    public string DictValue { get; set; } = string.Empty;

    /// <summary>标签样式（见 <see cref="Domain.Shared.Enums.TagType"/>）。</summary>
    public TagType TagType { get; set; }

    /// <summary>备注。</summary>
    public string? Description { get; set; }

    /// <summary>显示顺序（升序）。</summary>
    public int OrderNum { get; set; }
}

/// <summary>字典数据简项（GET /api/system/dict-data/simple/{dictType}，供前端下拉）。</summary>
public class DictDataSimpleVO
{
    /// <summary>字典标签（显示名称）。</summary>
    public string DictLabel { get; set; } = string.Empty;

    /// <summary>字典键值（存储值）。</summary>
    public string DictValue { get; set; } = string.Empty;

    /// <summary>标签样式（见 <see cref="Domain.Shared.Enums.TagType"/>）。</summary>
    public TagType TagType { get; set; }
}

/// <summary>字典数据写入请求基类：创建 / 更新共同字段（对齐 SysDictData 关联表实体）。</summary>
public abstract class DictDataWriteRequest : StatusDescriptionRequest
{
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

/// <summary>创建字典数据请求（POST /api/system/dict-data）。</summary>
public class DictDataCreateRequest : DictDataWriteRequest
{
}

/// <summary>更新字典数据请求（PUT /api/system/dict-data/{id}；类型可迁移，迁移后新旧类型缓存均失效）。</summary>
public class DictDataUpdateRequest : DictDataWriteRequest
{
}
