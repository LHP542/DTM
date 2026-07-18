using DTM.Data.Common;
using FluentAssertions;
using Xunit;

namespace DTM.Tests.Data;

public class AsyncUtilTests
{
    [Fact]
    public void RunSync_CompletedTask_Completes()
    {
        Action act = () => AsyncUtil.RunSync(() => Task.CompletedTask);
        act.Should().NotThrow();
    }

    [Fact]
    public void RunSync_T_ReturnsCorrectValue()
    {
        int result = AsyncUtil.RunSync(() => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public void RunSync_ExceptionTask_Propagates()
    {
        Action act = () => AsyncUtil.RunSync(
            () => Task.FromException(new InvalidOperationException("boom")));
        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public void RunSync_T_ExceptionTask_Propagates()
    {
        Action act = () => AsyncUtil.RunSync<int>(
            () => Task.FromException<int>(new ArgumentException("bad")));
        act.Should().Throw<ArgumentException>().WithMessage("bad");
    }
}
