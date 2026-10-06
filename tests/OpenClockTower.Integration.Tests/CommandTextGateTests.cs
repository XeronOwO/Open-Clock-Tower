using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 文本闸的口径（M4 / G-A5-8）：长度与控制字符在**入口处**判掉，豁免字段照旧通行。
/// </summary>
/// <remarks>
/// <para>
/// 审计点名的三处缺口逐条判：`reason` / `note` 连长度都没有、幂等键也没有长度、
/// 而且这些字符串会原样进事件流与日志。这里判的是闸本身；它是"客户端可控文本不许无界"
/// 这条约束的运行时判据，链路侧的判据在 <see cref="AbuseGuardHostTests"/>。
/// </para>
/// <para>
/// 另一条同样要紧：**豁免不是"没人管"**。豁免字段也要在这里判它们照常通行——
/// 否则"给所有 string 加闸"会以静默拒绝合法选项的方式回归玩法（那是比漏洞更快的翻车方式）。
/// </para>
/// </remarks>
public sealed class CommandTextGateTests
{
    [Fact]
    public void OverlongNote_IsRejected_WithHumanMessage()
    {
        var command = new VoidRequestCommand
        {
            RequestId = new OperationRequestId("request-1"),
            Reason = OperationRequestVoidReason.StorytellerForce,
            Note = new string('甲', CommandTextLimits.MaxFreeTextLength + 1),
        };

        var rejection = CommandTextGate.Check(Envelope(command));

        Assert.NotNull(rejection);
        Assert.Equal("legality.text_too_long", rejection.Code);
        Assert.Contains("说明", rejection.Message, StringComparison.Ordinal);
        // 说人话：文案里带上限与实际长度，玩家知道该删多少。
        Assert.Contains(CommandTextLimits.MaxFreeTextLength.ToString(), rejection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoteAtTheLimit_IsAccepted()
    {
        var command = new ForceAdvanceCommand { Reason = new string('甲', CommandTextLimits.MaxFreeTextLength) };

        Assert.Null(CommandTextGate.Check(Envelope(command)));
    }

    [Fact]
    public void ControlCharactersInFreeText_AreRejected_ButNewlinesAreFoldedNotRejected()
    {
        var vertical = new ForceAdvanceCommand { Reason = "甲\u0000乙" };
        var rejection = CommandTextGate.Check(Envelope(vertical));
        Assert.NotNull(rejection);
        Assert.Equal("legality.text_control_chars", rejection.Code);

        // 换行 / 制表算可折叠空白（与说书人注记同一把尺子）：闸放行，落库时折成空格。
        var multiline = new ForceAdvanceCommand { Reason = "甲\r\n乙\t丙" };
        Assert.Null(CommandTextGate.Check(Envelope(multiline)));
    }

    [Fact]
    public void OverlongIdempotencyKey_IsRejected_BeforeItReachesTheStore()
    {
        var command = new StartDayCommand();
        var rejection = CommandTextGate.Check(
            Envelope(command, new string('k', CommandTextLimits.MaxIdempotencyKeyLength + 1)));

        Assert.NotNull(rejection);
        Assert.Equal("legality.idempotency_key_too_long", rejection.Code);
    }

    [Fact]
    public void IdempotencyKeyWithControlCharacters_IsRejected()
    {
        var rejection = CommandTextGate.Check(Envelope(new StartDayCommand(), "key\nwith-newline"));

        Assert.NotNull(rejection);
        Assert.Equal("legality.idempotency_key_control", rejection.Code);
    }

    [Fact]
    public void AnnotationText_KeepsItsOwnRejectionCodes()
    {
        // 注记的超长 / 控制字符拒绝码由 D-0019 那批用例与文档钉住：闸与内核同尺，但不另起新码。
        var tooLong = CommandTextGate.Check(Envelope(new AddSeatAnnotationCommand
        {
            Seat = new SeatId(1),
            Text = new string('甲', SeatAnnotationText.MaxLength + 1),
        }));
        Assert.Equal("legality.annotation_too_long", Assert.IsType<CommandRejection>(tooLong).Code);

        var control = CommandTextGate.Check(Envelope(new AddSeatAnnotationCommand
        {
            Seat = new SeatId(1),
            Text = "甲\u0007乙",
        }));
        Assert.Equal("legality.annotation_control", Assert.IsType<CommandRejection>(control).Code);
    }

    [Fact]
    public void ArtistQuestion_RejectsEveryControlCharacter_IncludingNewlines()
    {
        // 单行字段用更严的一档：换行在这里也不接受（与 ArtistQuestionMachine 同尺）。
        var rejection = CommandTextGate.Check(Envelope(new AskArtistQuestionCommand { Question = "问\n题" }));

        Assert.Equal("artist.question_control", Assert.IsType<CommandRejection>(rejection).Code);
    }

    [Fact]
    public void ExemptOptionValues_PassThroughTheGate()
    {
        // 选项值不是自由文本：由服务端下发的合法集合判定，闸不拦（拦了就是玩法回归）。
        var submission = new SubmitResponseCommand
        {
            RequestId = new OperationRequestId("request-1"),
            OptionValue = new string('选', 400),
        };

        Assert.Null(CommandTextGate.Check(Envelope(submission)));
    }

    [Fact]
    public void CommandsWithoutClientText_PassThroughTheGate()
    {
        Assert.Null(CommandTextGate.Check(Envelope(new StartDayCommand())));
        Assert.False(CommandTextLimits.CarriesFreeText(new StartDayCommand()));
        Assert.True(CommandTextLimits.CarriesFreeText(new ForceAdvanceCommand { Reason = "x" }));
    }

    private static CommandEnvelope Envelope(GameCommand command, string key = "test-key") => new()
    {
        Command = command,
        Actor = Actor.Storyteller(),
        IdempotencyKey = key,
    };
}
