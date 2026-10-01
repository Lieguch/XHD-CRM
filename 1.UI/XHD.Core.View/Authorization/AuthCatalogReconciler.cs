using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XHD.Core.Models;

namespace XHD.Core.View.Authorization
{
    /// <summary>
    /// 启动期权限目录对账服务 —— Sprint 10.39「权限目录三处分裂」根治措施。
    /// </summary>
    /// <remarks>
    /// <h3>解决的问题</h3>
    /// 既有权限目录散落在三处且互不同步（<c>auth_inventory.md</c> 3.4）：
    /// <list type="bullet">
    /// <item>控制器魔法字符串 —— 92 个字面量，大小写混乱</item>
    /// <item><c>ConfigData/SysButtons.json</c> —— 56 条，字段 schema 与 <c>Sys_Button</c> 实体不匹配</item>
    /// <item><c>SeedData.cs</c> 硬编码 —— 53 条，且 <c>sys_authority</c> 表 0 处引用</item>
    /// </list>
    /// 审计实测后果：<b>11 个 auth_id 缺失、43 个从未被引用</b>。
    /// 这意味着：新建角色默认拿不到任何按钮权限（必须人工逐条勾选，极易漏配），
    /// 而代码里声明的权限在数据库里可能根本不存在。
    ///
    /// <h3>本服务如何做</h3>
    /// <ol>
    /// <li>启动时反射收集代码中全部 <see cref="ButtonAuthAttribute"/> 声明
    /// （<see cref="AuthCatalog.Current"/>）—— 这是唯一真源。</li>
    /// <li>与 <c>Sys_Button</c> 表现有主键求差集（<see cref="AuthCatalog.Reconcile"/>）。</li>
    /// <li><b>缺失的按钮幂等 upsert</b> ⇒ 新增一个 <c>[ButtonAuth]</c> 后，
    /// 重启即自动出现在权限配置界面，管理员可以为角色勾选。漂移在结构上不可能复现。</li>
    /// <li>孤儿按钮（DB 有、代码无人声明）只告警不删除 —— 删除会瞬间打断存量
    /// <c>Sys_authority</c> 角色绑定，属于破坏性操作，交由人工审核。</li>
    /// </ol>
    ///
    /// <h3>Fail-fast 的边界</h3>
    /// <list type="bullet">
    /// <item><b>反射收集结果为空</b> ⇒ 反射失败或程序集异常，<b>同步抛异常阻止启动</b>
    /// （权限目录为空等于完全放开权限，这是代码缺陷，必须硬失败）。</item>
    /// <item><b>数据库暂不可达</b> ⇒ 后台重试若干次，耗尽后<b>记录 ERROR 但不阻止启动</b>。
    /// 原因：本项目的数据库初始化（<c>DatabaseInitializerService</c>）在首次登录时才执行，
    /// 本服务往往是启动阶段第一个触达数据库的组件；若在 docker-compose 编排下
    /// 因 DB 慢半拍而抛异常，整个应用无法启动，代价远大于收益。
    /// 且 DB 不可达时应用本来就无法处理任何请求，提前失败并不带来安全增益。</item>
    /// <item>单纯存在缺失/孤儿按钮 <b>不</b>阻止启动 —— 缺失会自动补齐，孤儿只告警。</item>
    /// </list>
    ///
    /// <h3>启动不阻塞</h3>
    /// <see cref="StartAsync"/> 立即返回：对账在后台任务中执行，
    /// 避免 docker-compose 健康检查因等待数据库而超时。
    /// <see cref="StopAsync"/> 会等待后台任务完成（带超时），保证优雅关闭时不丢数据。
    ///
    /// <h3>可测性</h3>
    /// 对账核心逻辑在 <see cref="AuthCatalog.Collect"/> 与 <see cref="AuthCatalog.Reconcile"/>
    /// 中，均为纯函数（无 I/O），可由单元测试直接驱动。
    /// 本类只做编排与数据库交互。
    /// </remarks>
    public sealed class AuthCatalogReconciler : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AuthCatalogReconciler> _logger;

        private const int MaxRetries = 6;
        private const int RetryDelayMs = 2000;
        private const int StopWaitMs = 10000;

        private Task _backgroundWork;

        public AuthCatalogReconciler(
            IServiceScopeFactory scopeFactory,
            ILogger<AuthCatalogReconciler> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        /// <remarks>立即返回；实际对账在后台执行，避免阻塞应用启动与健康检查。</remarks>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            // 反射收集是纯函数，同步完成 —— 空目录属于代码缺陷，必须同步硬失败。
            var declared = AuthCatalog.Current;

            if (declared == null || declared.Count == 0)
            {
                throw new InvalidOperationException(
                    "[AuthCatalogReconciler] 反射未收集到任何 [ButtonAuth] 声明。" +
                    "权限目录为空等于完全放开权限，拒绝启动。" +
                    "请检查 AuthCatalog.Collect 的程序集加载与 ControllerBase 判定。");
            }

            _logger.LogInformation(
                "[AuthCatalogReconciler] 启动对账：代码声明 {Declared} 个 auth_id。",
                declared.Count);

            _backgroundWork = Task.Run(() => RunWithRetriesAsync(declared, cancellationToken),
                CancellationToken.None);

            return Task.CompletedTask;
        }

        private async Task RunWithRetriesAsync(IReadOnlyList<AuthCatalogEntry> declared, CancellationToken outerCt)
        {
            Exception lastError = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var fsql = scope.ServiceProvider.GetRequiredService<IFreeSql>();
                    await ReconcileAsync(fsql, declared, outerCt).ConfigureAwait(false);

                    _logger.LogInformation(
                        "[AuthCatalogReconciler] 对账完成（第 {Attempt}/{Max} 次尝试）。",
                        attempt, MaxRetries);
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    lastError = ex;
                    _logger.LogWarning(ex,
                        "[AuthCatalogReconciler] 第 {Attempt}/{Max} 次对账失败，{Delay}ms 后重试。",
                        attempt, MaxRetries, RetryDelayMs);
                    try { await Task.Delay(RetryDelayMs, outerCt).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }

            // 重试耗尽：记 ERROR 但不阻止启动（见类注释 Fail-fast 边界说明）。
            _logger.LogError(lastError,
                "[AuthCatalogReconciler] 连续 {Max} 次权限目录对账失败，本次启动未能补齐权限目录。" +
                "按钮权限检查将按数据库现状执行（可能缺少新声明的按钮），请检查数据库连接。",
                MaxRetries);
        }

        private async Task ReconcileAsync(IFreeSql fsql, IReadOnlyList<AuthCatalogEntry> declared, CancellationToken ct)
        {
            var existing = await fsql.Select<Sys_Button>().ToListAsync(b => b.id).ConfigureAwait(false);

            var report = AuthCatalog.Reconcile(declared, existing);

            // 缺失按钮补齐。注意：**不能** IgnoreColumns(id) —— 这里正是要把声明的
            // auth_id 作为主键写入。幂等性由前面 Select 出来的 existing 集合保证。
            foreach (var missing in report.Missing)
            {
                ct.ThrowIfCancellationRequested();

                var added = await fsql.Insert(new Sys_Button
                {
                    id = missing.AuthId,
                    Menu_id = missing.Menu,
                    Btn_name = $"{missing.Menu}.{missing.Operation}",
                    Btn_handler = $"{missing.Menu}.{missing.Operation}",
                    Btn_type = "button",
                    Btn_order = 100,
                    create_id = "system",
                    create_time = DateTime.Now
                }).ExecuteAffrowsAsync().ConfigureAwait(false);

                if (added <= 0)
                {
                    _logger.LogWarning(
                        "[AuthCatalogReconciler] 插入按钮 {AuthId} 返回影响行数 {Rows}（可能并发写入导致主键冲突）。",
                        missing.AuthId, added);
                }
            }

            if (report.Missing.Count > 0)
            {
                _logger.LogWarning(
                    "[AuthCatalogReconciler] 已补齐 {Count} 个缺失按钮：{Ids}",
                    report.Missing.Count,
                    string.Join(", ", report.Missing.Select(m => m.AuthId)));
            }

            if (report.Orphans.Count > 0)
            {
                _logger.LogWarning(
                    "[AuthCatalogReconciler] 发现 {Count} 个孤儿按钮（DB 中存在但代码无 [ButtonAuth] 声明），" +
                    "已保留不删除，请人工确认是否下线：{Ids}",
                    report.Orphans.Count,
                    string.Join(", ", report.Orphans));
            }

            _logger.LogInformation(
                "[AuthCatalogReconciler] 声明 {Declared} / DB 现有 {Existing} / 新增 {Added} / 孤儿 {Orphan}",
                report.Declared.Count, existing.Count, report.Missing.Count, report.Orphans.Count);
        }

        /// <inheritdoc/>
        /// <remarks>等待后台对账完成（带超时），避免优雅关闭时写入被中断。</remarks>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            var work = _backgroundWork;
            if (work == null) return;

            var completed = await Task.WhenAny(work, Task.Delay(StopWaitMs)).ConfigureAwait(false);
            if (completed != work)
            {
                _logger.LogWarning(
                    "[AuthCatalogReconciler] 后台对账在 {Ms}ms 内未结束，应用继续关闭。", StopWaitMs);
                return;
            }

            // 若后台任务以异常结束，在此重新抛出，让宿主感知
            await work.ConfigureAwait(false);
        }
    }
}
