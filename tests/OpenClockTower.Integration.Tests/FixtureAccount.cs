namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 一个夹具账号：登录名 / 玩家名 / 口令 / 账号会话。
/// </summary>
/// <remarks>
/// D-0027 之后"这一桌归谁"由账号认定，所以夹具也必须有一个**真账号**才谈得上进主持台。
/// 口令与会话都留在夹具里：需要"换个设备用同一账号回来"的用例要能重新登录（<see cref="Password"/>），
/// 进主持台则直接用会话（<see cref="AccountSession"/>）。
/// </remarks>
/// <param name="Id">账号标识（归属比对的依据）。</param>
/// <param name="Username">登录名。</param>
/// <param name="DisplayName">玩家名。</param>
/// <param name="Password">口令（明文只活在夹具里）。</param>
/// <param name="AccountSession">账号会话（注册即登录时签发）。</param>
public sealed record FixtureAccount(
    Application.AccountId Id,
    string Username,
    string DisplayName,
    string Password,
    string AccountSession);
