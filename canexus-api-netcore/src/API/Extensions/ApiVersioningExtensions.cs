using System.Globalization;
using Asp.Versioning;
using Microsoft.AspNetCore.Localization;

namespace API.Extensions;

/// <summary>API 版本化与请求本地化的服务注册扩展。</summary>
public static class ApiVersioningExtensions
{
    /// <summary>当前 API 版本常量（破坏性变更发布 v2 时在此登记新版本）；注意各控制器当前硬编码 [ApiVersion("1.0")] 而非引用本常量，两者需手工同步，后续泛型基类收敛时统一为引用本常量。</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>
    /// 注册 API 版本化：零破坏落法——默认版本 1.0 + 未指定版本时回退默认，
    /// 版本号可经查询串（api-version）或请求头（X-Api-Version）显式指定，
    /// 响应头 api-supported-versions 声明支持集；不引入 URL 版本段（避免对既有前端与测试的全部路径破坏），
    /// 未来发布 v2 时再按控制器粒度启用 URL 段路由。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApiVersioningSupport(this IServiceCollection services)
    {
        // AddMvc() 挂接 MVC 集成：[ApiVersion] 特性解析、版本协商过滤与 api-supported-versions 响应头回写
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = ApiVersionReader.Combine(
                new QueryStringApiVersionReader("api-version"),
                new HeaderApiVersionReader("X-Api-Version"));
        }).AddMvc();

        return services;
    }

    /// <summary>
    /// 注册请求本地化：支持 zh-CN（默认）与 en-US，按 Accept-Language 头协商；
    /// CurrentUICulture 由本中间件设置后，业务异常消息经 ErrorMessageLocalizer 按文化翻译。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddApiLocalization(this IServiceCollection services)
    {
        services.AddRequestLocalization(options =>
        {
            options.SetDefaultCulture("zh-CN");
            options.AddSupportedCultures("zh-CN", "en-US");
            options.AddSupportedUICultures("zh-CN", "en-US");
            // 仅 Accept-Language 协商（Cookie / 用户配置提供器后续按需扩展）
            options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
        });

        return services;
    }
}
