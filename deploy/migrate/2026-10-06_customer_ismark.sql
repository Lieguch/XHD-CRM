-- =============================================================================
-- XHDCRM3 客户跟进标记字段迁移：CRM_Customer.ismark（一次性）
-- 文件：deploy/migrate/2026-10-06_customer_ismark.sql
-- 方言：SQL Server（生产部署走 docker-compose.mssql.yml + db-init/entrypoint.sh）
--
-- 背景（根因）：
--   A 侧 CRM_Customer 表有 ismark 列（跟进状态标记：1=已跟进），
--   由「标记已跟进」按钮批量写入（A 侧 Server.CRM_Customer.UpdateBFmark ->
--   DAL.CRM_Customer.UpdateBFmark: update CRM_Customer set [ismark]=N where id in (...)）。
--   B 侧迁移时漏掉了该列，导致「标记已跟进」功能无落库字段。
--   Sprint 10.38 第 10 轮补齐全链路（Entity / Repository / Service / Controller / 视图），
--   生产库需先执行本脚本加列，B 侧应用才能写入 ismark。
--
-- 本脚本：为 [CRM_Customer] 增加 ismark 列（nullable int）。
--
-- ⚠️ 执行时机：
--   ★ 随 R10-B 版本发布前/发布时执行一次（应用上线前加列，避免 ORM 写入时报「列不存在」）。
--   ★ 若库中已存在该列（曾手工加过），本脚本因 IF NOT EXISTS 守卫而安全跳过。
--
-- 防误伤：
--   1. 只新增列，不修改/删除任何既有列或数据。
--   2. 列允许 NULL，既有行自动得到 NULL（语义=未跟进，前端仅 ismark===1 才显示「已跟进」），
--      与 A 侧未标记客户的展示一致，无需回填。
--   3. 不加默认值约束，避免与 ORM（FreeSql）映射的 int? 语义产生分歧。
--
-- 幂等：IF NOT EXISTS 守卫保证重复执行无副作用。
-- =============================================================================

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 0) 执行前核验：确认目标表存在、且尚无 ismark 列
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'[dbo].[CRM_Customer]')
      AND  name = N'ismark'
)
BEGIN
    -- 1) 加列：nullable int，既有行自动为 NULL（=未跟进）
    ALTER TABLE [dbo].[CRM_Customer]
        ADD [ismark] [int] NULL;
END

-- 2) 结果核验：ismark 列应已存在
SELECT COUNT(*) AS ismark_column_exists
FROM   sys.columns
WHERE  object_id = OBJECT_ID(N'[dbo].[CRM_Customer]')
  AND  name = N'ismark';
-- 期望输出：ismark_column_exists = 1

COMMIT TRANSACTION;
