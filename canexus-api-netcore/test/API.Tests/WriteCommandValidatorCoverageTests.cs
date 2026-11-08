
using API.Authorization;

namespace API.Tests;
/// <summary>写命令验证器覆盖的启动期自检测试：新增写命令若漏配 IValidator&lt;T&gt; 直接在此暴露。</summary>
[TestFixture]
public class WriteCommandValidatorCoverageTests
{
    /// <summary>扫描 Application.Contracts 中全部具体写命令，缺失验证器的清单应为空。</summary>
    [Test]
    public void AllWriteCommands_HaveValidators()
    {
        var missing = WriteCommandValidatorStartupValidator.FindUnvalidatedCommands(
            typeof(Application.Contracts.Abstractions.ICommand).Assembly,
            typeof(Application.Validators.Common.WriteCommandValidator<,>).Assembly);

        Assert.That(missing, Is.Empty, "以下写命令缺少验证器：\n" + string.Join("\n", missing));
    }
}
