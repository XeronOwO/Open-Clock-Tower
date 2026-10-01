using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 裁定点契约：有合法选项就等说书人；没有合法选项按声明的行为走，且**不抛异常**（R-0009）。
/// </summary>
public sealed class DecisionPointTests
{
    /// <summary>验收矩阵行 9：无合法选项 → 返回 OnNoOption 声明的行为。</summary>
    [Theory]
    [InlineData(NoOptionBehavior.Skip, DecisionPointOutcome.Skipped)]
    [InlineData(NoOptionBehavior.StorytellerDecides, DecisionPointOutcome.StorytellerDecides)]
    [InlineData(NoOptionBehavior.BlockAndAlert, DecisionPointOutcome.BlockedAndAlerted)]
    public void NoLegalOption_ReturnsDeclaredBehavior_WithoutThrowing(
        NoOptionBehavior declared,
        DecisionPointOutcome expected)
    {
        var point = new DecisionPoint
        {
            Id = new DecisionPointId("fortune-teller-night-1-red-herring"),
            Prompt = new ChoicePrompt
            {
                Context = "占卜师需要一名善良玩家作为干扰项，但场上没有善良玩家",
                Options = [],
                OnNoOption = declared,
            },
        };

        Assert.False(point.HasOptions);
        Assert.Equal(expected, point.Evaluate());
    }

    /// <summary>有合法选项 → 等待说书人裁定，选项与预览原样保留。</summary>
    [Fact]
    public void WithLegalOptions_AwaitsStorytellerChoice()
    {
        var point = new DecisionPoint
        {
            Id = new DecisionPointId("snake-charmer-night-1-target"),
            Prompt = new ChoicePrompt
            {
                Context = "舞蛇人选择一名玩家与自己交换角色",
                Options =
                [
                    new DecisionOption { Value = "seat:3", Preview = "3 号玩家与舞蛇人交换角色" },
                    new DecisionOption { Value = "seat:7", Preview = "7 号玩家与舞蛇人交换角色" },
                ],
                OnNoOption = NoOptionBehavior.Skip,
            },
        };

        Assert.True(point.HasOptions);
        Assert.Equal(DecisionPointOutcome.AwaitingChoice, point.Evaluate());
        Assert.Equal(2, point.Prompt.Options.Count);
        Assert.Equal("seat:3", point.Prompt.Options[0].Value);
        Assert.Equal("3 号玩家与舞蛇人交换角色", point.Prompt.Options[0].Preview);
    }

    /// <summary>未知声明不得静默跳过，也不得抛异常：按阻塞报警处理。</summary>
    [Fact]
    public void UnknownDeclaredBehavior_FallsBackToBlocking()
    {
        var point = new DecisionPoint
        {
            Id = new DecisionPointId("future-decision-point"),
            Prompt = new ChoicePrompt
            {
                Context = "尚未登记的无合法选项行为",
                Options = [],
                OnNoOption = (NoOptionBehavior)999,
            },
        };

        Assert.Equal(DecisionPointOutcome.BlockedAndAlerted, point.Evaluate());
    }
}
