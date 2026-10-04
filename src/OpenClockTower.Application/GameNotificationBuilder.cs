using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>把产出的事件翻译成需要推送的通知（推送在事件提交**之后**发出）。</summary>
public static class GameNotificationBuilder
{
    /// <summary>
    /// 翻译一次提交产出的事件；总是附带一条说书人视图变更通知。
    /// </summary>
    /// <param name="drafts">本批已提交的事件。</param>
    /// <param name="previousMachine">提交前的步骤机状态（找请求收件人用）。</param>
    /// <param name="publicSurfaceChanged">本批是否让公开生死面实际变化（由 <see cref="SessionTrackers.Update"/> 给出）。</param>
    /// <remarks>
    /// <para>
    /// 每条通知携带**背书事件的事件流序号**：客户端用它和快照序号比较先后并合并
    /// （票据 player-information-resync-race；此前推送无序号，补齐响应会覆盖窗口内到达的推送）。
    /// </para>
    /// <para>
    /// 只翻译业务事件：派生事件（对账补账）在下面的分支里没有匹配项，因此不产生推送；
    /// 但它们参与**末条序号**的判定——只有派生事件的提交不会让通知退回序号 0
    /// （0 是最小值，将来拿它做闸门会静默丢推送）。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<GameNotification> Build(
        IReadOnlyList<StoredEventDraft> drafts,
        StepMachineState? previousMachine,
        bool publicSurfaceChanged)
    {
        var notifications = new List<GameNotification>();
        long? lastDayEventSequence = null;
        long? lastLifeChangeSequence = null;
        long? lastViewRefreshSequence = null;
        foreach (var draft in drafts)
        {
            if (draft.Event is DayStartedEvent
                or NominationMadeEvent
                or VoteCastEvent
                or VoteSweepStartedEvent
                or SeatVoteCollectedEvent
                or VoteSweepResumedEvent
                or VoteCountedEvent
                or ExecutedEvent
                or DayClosedEvent)
            {
                lastDayEventSequence = draft.Sequence;
            }

            if (draft.Event is SeatStateChangedEvent { Life: not null })
            {
                // 公开面变化的背书事件；无白天事件的补推序号用它，而不是"本批最后一条草案"
                // （草案里可能跟着对账派生事件，口径与 lastDayEventSequence 保持一致）。
                lastLifeChangeSequence = draft.Sequence;
            }

            if (draft.Event is PhaseStartedEvent or DayClosedEvent)
            {
                // 阶段边界与白天收口都会改"我现在能不能动"（艺术家的提问权限位只在本人视图里，
                // 而它没有自己的推送通道）：记住最后一次边界序号，批末推一次按席位投影的整视图。
                lastViewRefreshSequence = draft.Sequence;
            }

            switch (draft.Event)
            {
                case OperationRequestIssuedEvent issued:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.OperationRequestIssued,
                        Sequence = draft.Sequence,
                        Seat = issued.Request.Addressee,
                        Request = issued.Request,
                    });
                    break;

                case InformationResultIssuedEvent information:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.InformationResultIssued,
                        Sequence = draft.Sequence,
                        Seat = information.Recipient,
                        Information = information,
                    });
                    break;

                case OperationRequestVoidedEvent voided:
                    var addressee = FindAddressee(drafts, previousMachine, voided.RequestId);
                    if (addressee is { } seat)
                    {
                        notifications.Add(new GameNotification
                        {
                            Kind = GameNotificationKind.OperationRequestVoided,
                            Sequence = draft.Sequence,
                            Seat = seat,
                            RequestId = voided.RequestId,
                            Void = voided.Void,
                        });
                    }

                    break;

                // 玩家本人作答与说书人代填走同一条通知：收件人始终是请求的行动者，
                // 「谁做出的决定」留在 Answer.Source 里（票据行 6 的可审计口径）。
                case OperationRequestAnsweredEvent answered:
                    var answeredAddressee = FindAddressee(drafts, previousMachine, answered.RequestId);
                    if (answeredAddressee is { } answeredSeat)
                    {
                        notifications.Add(new GameNotification
                        {
                            Kind = GameNotificationKind.OperationRequestAnswered,
                            Sequence = draft.Sequence,
                            Seat = answeredSeat,
                            RequestId = answered.RequestId,
                            Answer = answered.Answer,
                        });
                    }

                    break;

                // 阶段开始是公开信息：不带席位 → 分发器广播给全部已绑定席位。
                case PhaseStartedEvent started:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.PhaseStarted,
                        Sequence = draft.Sequence,
                        Phase = started.Plan.Phase,
                    });
                    break;

                // 游戏结束与呆瓜的公开选择都是公开事实：不带席位 → 广播（R-0024 / R-0027）。
                case GameEndedEvent ended:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.GameEnded,
                        Sequence = draft.Sequence,
                        Outcome = new GameOutcome
                        {
                            Winner = ended.Winner,
                            Condition = ended.Condition,
                            Detail = ended.Detail,
                        },
                    });
                    break;

                // 艺术家提问状态只在本人的视图里：提问 / 结清（回答、要求重问、强推作废）各推一次本人视图，
                // 否则入口与等待态只能等重连快照才更新（本装置首跑实测：白天开始后入口根本不出现）。
                case ArtistQuestionAskedEvent asked:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.PlayerViewChanged,
                        Sequence = draft.Sequence,
                        Seat = asked.Seat,
                    });
                    break;

                case ArtistQuestionClosedEvent closed:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.PlayerViewChanged,
                        Sequence = draft.Sequence,
                        Seat = closed.Seat,
                    });
                    break;

                case KlutzChoiceMadeEvent choice:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.KlutzChoiceMade,
                        Sequence = draft.Sequence,
                        KlutzChoice = choice,
                    });
                    break;

                // 旅行者加入 / 离场（D1）：公开事实——广播一次按席位投影的整视图（Seat=null），
                // 让两端刷新席位名单；重连时同一份事实由快照 + 事件补齐覆盖。
                case TravellerJoinedEvent:
                case TravellerDepartedEvent:
                    notifications.Add(new GameNotification
                    {
                        Kind = GameNotificationKind.PlayerViewChanged,
                        Sequence = draft.Sequence,
                    });
                    break;
            }
        }

        // 白天是公开信息：任何一条白天事件都折算成一条"白天状态已变化"（同批去重），
        // 序号取本批最后一条白天事件——那之后的白天状态才以它为界。
        if (lastDayEventSequence is { } daySequence)
        {
            notifications.Add(new GameNotification
            {
                Kind = GameNotificationKind.DayChanged,
                Sequence = daySequence,
            });
        }
        else if (publicSurfaceChanged && drafts.Count > 0)
        {
            // 没有白天事件背书、但公开生死面变了（如说书人白天上报生死；R-0022 第 2 条"白天变化即时公开"）：
            // 推一次读时投影，别让"已公开"退化成"下次刷新才看得见"。
            // 夜晚挂起不算公开面变化（SessionTrackers 只按公开投影的版本号判定）→ 这里不会推（D-0013 §5）。
            notifications.Add(new GameNotification
            {
                Kind = GameNotificationKind.DayChanged,
                Sequence = lastLifeChangeSequence ?? drafts[^1].Sequence,
            });
        }

        if (lastViewRefreshSequence is { } viewRefreshSequence)
        {
            // Seat=null = 推给全部已绑定席位（各自的那份投影）：白天开始会让艺术家的入口出现、
            // 白天收口 / 入夜会让它消失，都不该等重连才发现。
            notifications.Add(new GameNotification
            {
                Kind = GameNotificationKind.PlayerViewChanged,
                Sequence = viewRefreshSequence,
            });
        }

        notifications.Add(new GameNotification
        {
            Kind = GameNotificationKind.StorytellerViewChanged,
            Sequence = drafts.Count > 0 ? drafts[^1].Sequence : 0,
        });
        return notifications;
    }

    private static SeatId? FindAddressee(
        IReadOnlyList<StoredEventDraft> drafts,
        StepMachineState? previousMachine,
        OperationRequestId requestId)
    {
        foreach (var draft in drafts)
        {
            if (draft.Event is OperationRequestIssuedEvent issued && issued.Request.Id == requestId)
            {
                return issued.Request.Addressee;
            }
        }

        var pending = previousMachine?.PendingRequest;
        return pending is not null && pending.Id == requestId ? pending.Addressee : null;
    }
}
