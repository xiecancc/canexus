
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace API.Extensions;
/// <summary>Swagger / OpenAPI 文档的依赖注册与中间件扩展。</summary>
public static class SwaggerDependencyExtensions
{
    /// <summary>
    /// 注册 OpenAPI 文档服务：
    /// <list type="bullet">
    /// <item>文档由 Microsoft.AspNetCore.OpenApi 生成（/openapi/v1.json）；</item>
    /// <item>文档转换器注入 Bearer 鉴权方案（UI 出现 Authorize 按钮，可粘贴 accessToken 调试受保护接口）。</item>
    /// </list>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "JWT 授权：粘贴 /api/auth/login 返回的 accessToken，前缀 Bearer 由 UI 自动添加"
                };
                return Task.CompletedTask;
            });
        });

        return services;
    }

    /// <summary>
    /// 启用 Swagger 文档管道（仅开发环境）：
    /// <list type="bullet">
    /// <item>/openapi/v1.json：OpenAPI 3.1 文档端点；</item>
    /// <item>/swagger：Swagger UI，加载上述文档。</item>
    /// </list>
    /// </summary>
    /// <param name="app">Web 应用。</param>
    /// <returns>Web 应用。</returns>
    public static WebApplication UseSwaggerDocumentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "Super Admin API v1");
                options.DocumentTitle = "Super Admin API";
            });
        }

        return app;
    }
}
