using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;

using FreeSql;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using XHD.Core.Common;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 10.38：公告/新闻合并表 Message_news 的 is_notice 区分验证。
    /// 背景：B 版把 A 版 public_news + public_notice 合并为单表 Message_news
    /// （db-schema-map.md:87/145），靠 is_notice 区分两类记录。
    /// 本文件覆盖：NoticeRemind 仅返回未读公告、NewsRemind 仅返回新闻、
    /// Grid 的 is_notice 查询参数、详情 Detail action。
    /// </summary>
    public class MessageNewsNoticeTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Message_newsRepository _newsRepo;

        public MessageNewsNoticeTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _newsRepo = new Message_newsRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static Message_news NewItem(string id, bool is_notice, bool isRead = false, DateTime? createTime = null)
        {
            return new Message_news
            {
                id = id,
                create_id = "ADMIN",
                create_time = createTime ?? new DateTime(2024, 6, 15),
                news_title = $"{(is_notice ? "公告" : "新闻")}-{id}",
                news_content = "&lt;p&gt;内容&lt;/p&gt;",
                isRead = isRead,
                read_time = isRead ? DateTime.Now : null,
                is_notice = is_notice
            };
        }

        private async Task InsertAsync(Message_news n) => await _fsql.Insert(n).ExecuteAffrowsAsync();

        // ============ Controller 装配辅助 ============

        private MessageNewsController CreateController(string userId = "TEST_USER", string queryString = "")
        {
            var newsSvc = new Mock<IMessage_newsService>();
            newsSvc.Setup(s => s.NoticeRemindAsync(It.IsAny<int>()))
                .Returns((int limit) => _newsRepo.NoticeRemindAsync(limit));
            newsSvc.Setup(s => s.NewsRemindAsync(It.IsAny<int>()))
                .Returns((int limit) => _newsRepo.NewsRemindAsync(limit));
            newsSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Message_news, bool>>>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Expression<Func<Message_news, bool>> e, int p, int l) => _newsRepo.GridAsync(e, p, l));
            newsSvc.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Message_news, bool>>>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Message_news, bool>> e, int p, int l, string ob) => _newsRepo.GridAsync(e, p, l, ob));

            var logMock = new Mock<ISys_logService>();
            logMock.Setup(l => l.UpdateLog(It.IsAny<Sys_log>())).ReturnsAsync(1);
            logMock.Setup(l => l.DeleteLog(It.IsAny<Sys_log>())).ReturnsAsync(1);

            var ctrl = TestControllerHelper.CreateWithHttpContext<MessageNewsController>(
                queryString, userId, "Test User",
                new Mock<Microsoft.Extensions.Logging.ILogger<MessageNewsController>>().Object,
                newsSvc.Object,
                logMock.Object,
                new Mock<IDBAuthService>().Object);

            return ctrl;
        }

        // ============ is_notice 区分：NoticeRemind / NewsRemind ============

        [Fact]
        public async Task NoticeRemind_OnlyReturnsUnreadNotices()
        {
            // 未读公告 + 未读新闻混合：NoticeRemind 只应返回公告
            await InsertAsync(NewItem("N1", is_notice: true, createTime: new DateTime(2024, 6, 3)));
            await InsertAsync(NewItem("N2", is_notice: true, isRead: true, createTime: new DateTime(2024, 6, 2)));
            await InsertAsync(NewItem("W1", is_notice: false, createTime: new DateTime(2024, 6, 1)));

            var ctrl = CreateController();

            var json = await ctrl.NoticeRemind();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("N1", (string)data[0]["id"]!);
            Assert.True((bool)data[0]["is_notice"]!);
        }

        [Fact]
        public async Task NewsRemind_OnlyReturnsNewsItems()
        {
            // 新闻提醒与公告提醒内容必须分离（合并表之前两面板重叠的根因）
            await InsertAsync(NewItem("W1", is_notice: false, createTime: new DateTime(2024, 6, 3)));
            await InsertAsync(NewItem("W2", is_notice: false, createTime: new DateTime(2024, 6, 1)));
            await InsertAsync(NewItem("N1", is_notice: true, createTime: new DateTime(2024, 6, 2)));

            var ctrl = CreateController();

            var json = await ctrl.NewsRemind(limit: 5);
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Equal(2, data.Count);
            Assert.All(data, d => Assert.False((bool)d["is_notice"]!));
            // 按 create_time desc：W1 在前
            Assert.Equal("W1", (string)data[0]["id"]!);
        }

        [Fact]
        public async Task NoticeRemind_AndNewsRemind_DoNotOverlap()
        {
            // 合并表的核心验收点：两类提醒返回集合互斥
            await InsertAsync(NewItem("N1", is_notice: true, createTime: new DateTime(2024, 6, 2)));
            await InsertAsync(NewItem("W1", is_notice: false, createTime: new DateTime(2024, 6, 1)));

            var ctrl = CreateController();

            var noticeJson = await ctrl.NoticeRemind();
            var noticeIds = ((JArray)JObject.Parse(noticeJson)["data"]!).Select(d => (string)d["id"]!).ToList();

            var newsJson = await ctrl.NewsRemind();
            var newsIds = ((JArray)JObject.Parse(newsJson)["data"]!).Select(d => (string)d["id"]!).ToList();

            Assert.Contains("N1", noticeIds);
            Assert.DoesNotContain("N1", newsIds);
            Assert.Contains("W1", newsIds);
            Assert.DoesNotContain("W1", noticeIds);
        }

        // ============ Grid 的 is_notice 查询参数 ============

        [Fact]
        public async Task Grid_WithIsNoticeTrue_ReturnsOnlyNotices()
        {
            await InsertAsync(NewItem("N1", is_notice: true));
            await InsertAsync(NewItem("W1", is_notice: false));

            var ctrl = CreateController(queryString: "?is_notice=true");

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Message_news>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            var data = (JArray)obj["data"]!;
            Assert.Single(data);
            Assert.Equal("N1", (string)data[0]["id"]!);
        }

        [Fact]
        public async Task Grid_WithoutFilter_ReturnsAll()
        {
            await InsertAsync(NewItem("N1", is_notice: true));
            await InsertAsync(NewItem("W1", is_notice: false));

            var ctrl = CreateController();

            var json = await ctrl.Grid(TestControllerHelper.BuildPageView<Message_news>());
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, ((JArray)obj["data"]!).Count);
        }

        // ============ 详情 Detail action ============

        [Fact]
        public async Task Detail_ValidNoticeId_ReturnsModel()
        {
            await InsertAsync(NewItem("N1", is_notice: true));

            var ctrl = CreateController();

            var result = await ctrl.Detail("N1");

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<Message_news>(viewResult.ViewData["model"]);
            Assert.Equal("N1", model.id);
            Assert.True(model.is_notice);
            Assert.Equal("公告-N1", model.news_title);
        }

        [Fact]
        public async Task Detail_ValidNewsId_ReturnsModel()
        {
            await InsertAsync(NewItem("W1", is_notice: false));

            var ctrl = CreateController();

            var result = await ctrl.Detail("W1");

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<Message_news>(viewResult.ViewData["model"]);
            Assert.False(model.is_notice);
        }

        [Fact]
        public async Task Detail_UnknownId_ReturnsNotFound()
        {
            var ctrl = CreateController();

            var result = await ctrl.Detail("NOT_EXIST");

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Detail_EmptyId_ReturnsNotFound()
        {
            var ctrl = CreateController();

            var result = await ctrl.Detail("");

            Assert.IsType<NotFoundResult>(result);
        }

        // ============ Schema 字段存在性 ============

        [Fact]
        public void Schema_Message_news_HasIsNoticeField()
        {
            var prop = typeof(Message_news).GetProperty("is_notice");
            Assert.NotNull(prop);
            Assert.Equal(typeof(bool), prop.PropertyType);
        }
    }
}
