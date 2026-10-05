-- =============================================================================
-- XHDCRM3 数据权限语义翻转迁移：Sys_role.DataAuth 4 -> 5（一次性）
-- 文件：deploy/migrate/2026-10-05_dataauth_4_to_5.sql
-- 方言：SQL Server（生产部署走 docker-compose.mssql.yml + db-init/entrypoint.sh）
--
-- 背景（根因）：
--   Sprint 10.38 第 6 轮把 Sys_role.DataAuth 语义从 B 版旧契约（0-4，4=全部）
--   翻转为 A 版契约（0-5，4=指定部门 / 5=全部）。翻转前落库的旧数据里，
--   DataAuth=4 表示「全部」，翻转后同一值 4 被解释为「指定部门」，
--   会导致原本拥有全部数据权限的角色被错误收窄为「无可见数据」。
--
-- 本脚本把「旧语义的 4」纠正为「新语义的 5（全部）」。
--
-- ⚠️ 执行时机（关键，防止误伤）：
--   ★ 仅在「从翻转前版本首次升级到翻转后版本」这一窗口执行一次。
--   ★ 翻转后若已有角色被新建为「指定部门」（合法新 4），本脚本会误伤 ——
--     因此必须在翻转后首个版本发布、且尚无新语义 4 数据时执行。
--   ★ 若已错过该窗口（已存在合法 4），改用下方「安全核验」SELECT 先人工甄别，
--     只对确认为旧语义的角色执行 UPDATE。
--
-- 防误伤三重条件：
--   1. DataAuth = 4           —— 仅旧语义的「全部」
--   2. id != 'SystemAdminRole' —— admin 角色不受影响（GetDataAuth 对 admin 硬编码 authtype=5）
--   3. ISNULL(isDelete,0) = 0 —— 仅存活角色，已删除的无需迁移
--
-- 幂等：重复执行无副作用（已置 5 的行不再命中 WHERE DataAuth=4）。
-- =============================================================================

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 0) 安全核验：先查看将要迁移的行（人工确认后再执行下方 UPDATE）
SELECT id, RoleName, DataAuth, isDelete
FROM   [Sys_role]
WHERE  DataAuth = 4
  AND  id <> 'SystemAdminRole'
  AND  ISNULL(isDelete, 0) = 0;

-- 1) 执行迁移：旧语义「全部」(4) -> 新语义「全部」(5)
UPDATE [Sys_role]
SET    DataAuth = 5
WHERE  DataAuth = 4
  AND  id <> 'SystemAdminRole'
  AND  ISNULL(isDelete, 0) = 0;

-- 2) 结果核验：迁移后应无 DataAuth=4 的非 admin 存活角色
SELECT COUNT(*) AS remaining_dataauth4_nonadmin
FROM   [Sys_role]
WHERE  DataAuth = 4
  AND  id <> 'SystemAdminRole'
  AND  ISNULL(isDelete, 0) = 0;
-- 期望输出：remaining_dataauth4_nonadmin = 0

COMMIT TRANSACTION;
