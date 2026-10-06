namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 库的运维底座门禁（M5 / G-A6-4 · G-A6-6 · G-A7-4）：运行身份、库权限、热备份与恢复演练
/// 只要少一样，陌生人照抄部署就会得到"以 root 跑、库对全机可读、根本没有备份"的站点。
/// </summary>
/// <remarks>
/// 与反代模板那条门禁同一个道理：判的是**文本存在性**，不判 systemd / shell 的语法
/// （那要一台跑着 systemd 的机器，属真机读数）。存在性门禁的价值在于：删掉某一条时，
/// 必须在测试里显式删掉这一行——那时他至少会看见"这是有意为之的"。
/// </remarks>
public sealed class DatabaseOperationsGateTests
{
    private static string ServiceTemplate => Template("clocktower.service.template");

    private static string BackupServiceTemplate => Template("clocktower-backup.service.template");

    private static string BackupTimerTemplate => Template("clocktower-backup.timer.template");

    private static string DrillScript =>
        File.ReadAllText(RepositoryLayout.PathOf("tools", "deploy", "restore-drill.sh"));

    private static string DeployDocument =>
        File.ReadAllText(RepositoryLayout.PathOf("docs", "operations", "deploy.md"));

    private static string DeployPrepare =>
        File.ReadAllText(RepositoryLayout.PathOf("tools", "deploy-prepare.mjs"));

    /// <summary>服务单元：专用运行用户 + 只有属主可写的 umask + 最小权限。</summary>
    [Fact]
    public void ServiceUnit_RunsAsDedicatedUserWithOwnerOnlyFiles()
    {
        var template = ServiceTemplate;
        string[] required =
        [
            "User={{RUN_USER}}",
            "Group={{RUN_USER}}",
            "UMask=0077",
            "NoNewPrivileges=true",
            "ProtectSystem=full",
            "ProtectHome=true",
            // 工作目录与入口仍然是应用目录（页面目录跟着工作目录走）。
            "WorkingDirectory={{APP_DIR}}",
            "ExecStart={{APP_DIR}}/OpenClockTower.Server",
        ];

        var missing = required.Where(line => !template.Contains(line, StringComparison.Ordinal)).ToList();
        Assert.True(
            missing.Count == 0,
            "服务单元模板少了运行身份 / 权限条款（M5 / G-A6-6 · G-A7-4）：" + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
        // 明文写死 root 等于把这条加固又拆掉，而且看不出是谁拆的。
        Assert.DoesNotContain("User=root", template, StringComparison.Ordinal);
    }

    /// <summary>备份单元与定时器：跑的就是维护命令，且同样是专用用户 + 显式库路径。</summary>
    [Fact]
    public void BackupUnits_CallTheMaintenanceCommandAsTheDedicatedUser()
    {
        var service = BackupServiceTemplate;
        string[] required =
        [
            "User={{RUN_USER}}",
            "UMask=0077",
            // 库路径显式写出：定时任务"备的是哪个库"必须一眼可见。
            "backup --db {{APP_DIR}}/data/oct.db",
            "--out {{BACKUP_DIR}}",
            "--keep {{BACKUP_KEEP}}",
        ];

        var missing = required.Where(line => !service.Contains(line, StringComparison.Ordinal)).ToList();
        Assert.True(
            missing.Count == 0,
            "热备份单元少了关键项（M5 / G-A6-4）：" + Environment.NewLine + string.Join(Environment.NewLine, missing));

        var timer = BackupTimerTemplate;
        Assert.Contains("OnCalendar={{BACKUP_CALENDAR}}", timer, StringComparison.Ordinal);
        // 停机错过的备份要补跑：备份的连续性不该被一次重启吃掉。
        Assert.Contains("Persistent=true", timer, StringComparison.Ordinal);
        Assert.Contains("WantedBy=timers.target", timer, StringComparison.Ordinal);
    }

    /// <summary>
    /// 恢复演练脚本：只在临时目录里恢复、起临时实例，**绝不碰正在服务的库**。
    /// </summary>
    [Fact]
    public void DrillScript_RestoresIntoATemporaryDirectoryAndNeverTouchesTheLiveDatabase()
    {
        var script = DrillScript;
        string[] required =
        [
            "mktemp -d",
            // 要的是**真的调用**那一行：只写"db-report"会被注释或说明文字满足，门禁就白设了（实测踩到）。
            "OpenClockTower.Server\" db-report --db",
            "--backup",
            "/healthz",
            // 恢复出来的库只有属主可读（与生产同一个规矩）。
            "chmod 600",
        ];

        var missing = required.Where(line => !script.Contains(line, StringComparison.Ordinal)).ToList();
        Assert.True(
            missing.Count == 0,
            "恢复演练脚本少了关键步骤（M5 / G-A6-4）：" + Environment.NewLine + string.Join(Environment.NewLine, missing));

        // 这条是脚本的安全边界：它只往演练目录里写，任何写回生产库的路径都不许出现。
        Assert.DoesNotContain("\"$app_dir/data/oct.db\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("cp \"$backup_file\" \"$app_dir", script, StringComparison.Ordinal);
    }

    /// <summary>三份配置都要由同一条生成命令产出：模板改了、生成器忘了接，部署的人就少拿到一份。</summary>
    [Fact]
    public void DeployPrepare_RendersEveryTemplateItPromises()
    {
        var script = DeployPrepare;
        string[] required =
        [
            "'clocktower.service.template'",
            "'clocktower-backup.service.template'",
            "'clocktower-backup.timer.template'",
            "'{{RUN_USER}}'",
            "'{{BACKUP_KEEP}}'",
            // 安装说明与模板一起生成，避免"文档说的"和"脚本打的"两套。
            "chmod 600",
            "chmod 755",
            "restore-drill.sh",
        ];

        var missing = required.Where(line => !script.Contains(line, StringComparison.Ordinal)).ToList();
        Assert.True(
            missing.Count == 0,
            "生成脚本没有把运维底座的产物 / 步骤接上（M5）：" + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// 部署文档要把"备份怎么做、怎么恢复、怎么回滚、WAL 是什么口径"写出来——
    /// 这四条是**陌生人唯一的信息来源**，缺一条他就得猜。
    /// </summary>
    [Fact]
    public void DeployDocument_DocumentsBackupRestoreRollbackAndJournalMode()
    {
        var document = DeployDocument;
        string[] required =
        [
            // 备份（命令 + 不停服 + 轮转 + 异地）
            "backup \\",
            "--keep 7",
            "不停服",
            "轮转",
            "rsync",
            // 恢复与演练
            "### 6.3 恢复",
            "### 6.4 恢复演练（没演练过的备份不算备份）",
            "restore-drill.sh",
            "db-report",
            // 库权限与运行身份
            "chmod 600",
            "chmod 700",
            // 入口程序的执行位要给运行用户：u+x 会得到 744，进程换成专用用户后就是 203/EXEC（真机踩过）。
            "chmod 755 <APP_DIR>/OpenClockTower.Server",
            "UMask=0077",
            "useradd --system",
            // 口径与运营
            "### 6.5 关于 WAL",
            "### 9.2 重启对正在进行的对局意味着什么",
            "### 9.3 回滚",
        ];

        var missing = required.Where(line => !document.Contains(line, StringComparison.Ordinal)).ToList();
        Assert.True(
            missing.Count == 0,
            "部署文档少了运维底座的条目（M5）：" + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    private static string Template(string name) =>
        File.ReadAllText(RepositoryLayout.PathOf("tools", "deploy", "templates", name));
}
