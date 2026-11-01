
using Application.Contracts.Common;
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
public class DictTypeListResponse : AggregatePagedResponse
{
    /// <summary>字典名称。</summary>
    public string DictName { get; set; } = string.Empty;

    /// <summary>字典类型（唯一）。</summary>
    public string DictType { get; set; } = string.Empty;

}

/// <summary>字典类型写入请求基类：创建 / 更新共同字段（对齐 SysDictType 聚合；DictType 创建后不可变更；挂接创建请求根）。</summary>
public abstract class DictTypeRequest : AggregateChangeRequest
{
    /// <summary>字典名称。</summary>
    public string DictName { get; set; } = string.Empty;

    /// <summary>字典类型（唯一）。</summary>
    public string DictType { get; set; } = string.Empty;

    /// <summary>备注。</summary>
    public string? Description { get; set; }
}

/// <summary>创建字典类型请求（POST /api/system/dict-type）。</summary>
public class DictTypeCreateRequest : DictTypeRequest
{
}

/// <summary>更新字典类型请求（PUT /api/system/dict-type/{id}；DictType 不可变更；Version 须回传乐观锁版本号）。</summary>
public class DictTypeUpdateRequest : DictTypeRequest
{
}

/// <summary>删除字典类型请求（DELETE /api/system/dict-type/{id}；Version 须回传乐观锁版本号）。</summary>
public class DictTypeDeleteRequest
{
    /// <summary>乐观锁版本号（须与聚合当前版本一致才允许删除）。</summary>
    public Guid Version { get; set; }
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
public class DictDataListResponse : AggregatePagedResponse
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

/// <summary>字典数据简项（GET /api/system/dict-data/simple/{dictType}，供前端下拉）。</summary>
public class DictDataSimpleResponse : AggregateListResponse
{
    /// <summary>字典标签（显示名称）。</summary>
    public string DictLabel { get; set; } = string.Empty;

    /// <summary>字典键值（存储值）。</summary>
    public string DictValue { get; set; } = string.Empty;

    /// <summary>标签样式（见 <see cref="Domain.Shared.Enums.TagType"/>）。</summary>
    public TagType TagType { get; set; }
}

/// <summary>字典数据写入请求基类：创建 / 更新共同字段（镜像 SysDictData 聚合的启停 / 备注与业务字段）。</summary>
public abstract class DictDataRequest : AggregateChangeRequest
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

/// <summary>创建字典数据请求（POST /api/system/dict-data）。</summary>
public class DictDataCreateRequest : DictDataRequest
{
}

/// <summary>更新字典数据请求（PUT /api/system/dict-data/{id}；类型可迁移，迁移后新旧类型缓存均失效；Version 须回传乐观锁版本号）。</summary>
public class DictDataUpdateRequest : DictDataRequest
{
}

/// <summary>删除字典数据请求（DELETE /api/system/dict-data/{id}；Version 须回传乐观锁版本号）。</summary>
public class DictDataDeleteRequest
{
    /// <summary>乐观锁版本号（须与聚合当前版本一致才允许删除）。</summary>
    public Guid Version { get; set; }
}

/// <summary>按字典类型查询字典数据请求（载荷即字典类型编码）。</summary>
public class DictDataByTypeRequest(string DictType) : AggregateListRequest
{
    /// <summary>字典类型编码。</summary>
    public string DictType { get; } = DictType;
}
