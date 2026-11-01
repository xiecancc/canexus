
using Application.Mappings;
using AutoMapper;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Application.Tests.Mappings;
/// <summary>AutoMapper 全量映射配置兜底校验：应用启动经 AddMaps 扫描 Application 程序集注册全部 Profile，
/// 此处以同一口径构建配置并执行 <see cref="MapperConfiguration.AssertConfigurationIsValid"/>，
/// 防止映射成员拼写漂移、目标成员漏配在运行期才暴露（CI 前置兜底）。
/// 处理器手工组装的字段（如列表的部门名 / 角色集合、公告正文）须在对应 Profile 显式 Ignore 并注释说明。</summary>
[TestFixture]
public class MappingConfigurationTests
{
    [Test]
    public void AutoMapperConfiguration_IsValid()
    {
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var config = new MapperConfiguration(
            cfg => cfg.AddMaps(typeof(CommonProfile).Assembly),
            loggerFactory);

        Assert.DoesNotThrow(() => config.AssertConfigurationIsValid());
    }
}
