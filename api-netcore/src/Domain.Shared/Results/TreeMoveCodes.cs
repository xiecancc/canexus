namespace Domain.Shared.Results;

/// <summary>
/// 树形节点移动（<c>IAggregateTreeRepository&lt;T&gt;.MoveAsync</c>）的业务错误码与实体名定制。
/// <para>缺省使用通用段错误码；具体模块（部门 / 菜单等）传入自己的分段码，保持既有 API 契约稳定。</para>
/// </summary>
/// <param name="ParentInvalid">父级不合法（自身或自身后代）的业务错误码。</param>
/// <param name="NodeNotFound">待移动节点不存在的业务错误码。</param>
/// <param name="ParentNotFound">父级节点不存在的业务错误码。</param>
/// <param name="EntityName">实体显示名（用于异常消息文案，如「部门」「菜单」）。</param>
public sealed record TreeMoveCodes(
    int ParentInvalid = ErrorCodes.CommonTreeParentInvalid,
    int NodeNotFound = ErrorCodes.CommonDataNotFound,
    int ParentNotFound = ErrorCodes.CommonDataNotFound,
    string EntityName = "树形节点");
