
using Application.Contracts.Commands;
using Application.Contracts.Menus;
using Application.Validators.Common;
using Domain.Shared.Enums;
using Domain.Shared.Exceptions;
using Domain.Shared.Results;
using FluentValidation;

namespace Application.Validators;
/// <summary>菜单写入请求验证器：菜单类型 / 名称 / 状态 / 显示标记取值，
/// 以及类型与字段耦合的跨属性规则（目录必有 path、菜单必有 path 与 component、按钮必有 perms）。
/// 错误码保持 MenuRuleInvalid（400404）与原服务层文案一致；创建 / 更新共用（消除重复定义）。</summary>
public class MenuRequestValidator<TRequest> : AbstractValidator<TRequest>
    where TRequest : MenuInputRequest
{
    /// <summary>构造函数。</summary>
    public MenuRequestValidator()
    {
        RuleFor(request => request)
            .Custom((request, context) => ValidateMenuFields(
                request.MenuType, request.MenuName, request.Path, request.Component, request.Perms, request.Visible, request.Status));
    }

    /// <summary>菜单字段耦合校验（错误码与文案逐字保持原服务层语义）。</summary>
    private static void ValidateMenuFields(MenuType menuType, string menuName, string? path, string? component, string? perms, MenuVisible visible, CommonStatus status)
    {
        if (string.IsNullOrWhiteSpace(menuName))
        {
            throw new BusinessException(ErrorCodes.MenuRuleInvalid, "菜单名称不能为空");
        }

        if (menuType is not (MenuType.Directory or MenuType.Menu or MenuType.Button))
        {
            throw new BusinessException(ErrorCodes.MenuRuleInvalid, $"菜单「{menuName}」类型不合法：仅支持 1 目录 / 2 菜单 / 3 按钮");
        }

        if (menuType == MenuType.Directory && string.IsNullOrWhiteSpace(path))
        {
            throw new BusinessException(ErrorCodes.MenuRuleInvalid, $"目录「{menuName}」必须设置路由地址（path）");
        }

        if (menuType == MenuType.Menu
            && (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(component)))
        {
            throw new BusinessException(ErrorCodes.MenuRuleInvalid, $"菜单「{menuName}」必须同时设置路由地址（path）与组件路径（component）");
        }

        if (menuType == MenuType.Button && string.IsNullOrWhiteSpace(perms))
        {
            throw new BusinessException(ErrorCodes.MenuRuleInvalid, $"按钮「{menuName}」必须设置权限标识（perms）");
        }

        if (status is not (CommonStatus.Enabled or CommonStatus.Disabled))
        {
            throw new BusinessException(ErrorCodes.MenuRuleInvalid, "菜单状态取值不合法：仅支持 1 启用 / 0 停用");
        }

        if (visible is not (MenuVisible.Show or MenuVisible.Hide))
        {
            throw new BusinessException(ErrorCodes.MenuRuleInvalid, "菜单显示标记取值不合法：仅支持 1 显示 / 0 隐藏");
        }
    }
}

/// <summary>创建菜单命令验证器（桥接请求验证器）；父菜单存在性与层级深度由处理器校验。</summary>
public sealed class MenuCreateCommandValidator() :
    WriteCommandValidator<MenuCreateCommand, MenuInputRequest>(new MenuRequestValidator<MenuInputRequest>());

/// <summary>更新菜单命令验证器（桥接请求验证器）；父级不可选自身或后代由处理器校验。</summary>
public sealed class MenuUpdateCommandValidator() :
    UpdateCommandValidator<MenuUpdateCommand, MenuInputRequest>(new MenuRequestValidator<MenuInputRequest>());

/// <summary>删除菜单命令验证器（乐观锁版本号入口兜底）。</summary>
public sealed class MenuDeleteCommandValidator() : DeleteCommandValidator<MenuDeleteCommand>;
