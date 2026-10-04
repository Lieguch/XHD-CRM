using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

using FreeSql;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

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
    /// Sprint 10.38 难点线：CustomerAttaController 附件上传真测试。
    /// 覆盖普通上传（路径遍历防护 + 落盘 + CRM_Customer_atta 落库）、
    /// 分片上传（分片落盘 + guid/attId 遍历防护）、Meger 合并（产物 + 落库 + 分片清理）、
    /// Save、Del（记录删除 + 物理文件删除）、Grid（按 cus_id 过滤 + 数据权限）。
    /// 全部走真实 SQLite 内存库 + 真实 CRM_Customer_attaRepository + 真实文件 IO（隔离在临时目录）。
    /// </summary>
    public class CustomerAttaUploadTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly CRM_Customer_attaRepository _repo;
        private readonly string _origDir;
        private readonly string _tmpDir;

        public CustomerAttaUploadTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _repo = new CRM_Customer_attaRepository(_fsql);

            // 真实文件 IO 隔离在临时目录，绝不写进仓库目录
            _origDir = Directory.GetCurrentDirectory();
            _tmpDir = Path.Combine(Path.GetTempPath(), "xhdatta_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tmpDir);
            Directory.SetCurrentDirectory(_tmpDir);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_origDir);
            if (Directory.Exists(_tmpDir))
            {
                try { Directory.Delete(_tmpDir, true); } catch { }
            }
            _fsql?.Dispose();
        }

        // ============ 辅助 ============

        /// <summary>
        /// 字典版 ISession：Upload/Meger 从 Session.GetString("newCustomerID") 读客户 ID。
        /// </summary>
        private sealed class DictSession : ISession
        {
            private readonly Dictionary<string, byte[]> _store = new Dictionary<string, byte[]>();

            public string Id => "test-session";
            public bool IsAvailable => true;
            public IEnumerable<string> Keys => _store.Keys;

            public Task LoadAsync() => Task.CompletedTask;
            public Task CommitAsync() => Task.CompletedTask;
            public Task ClearAsync()
            {
                _store.Clear();
                return Task.CompletedTask;
            }
            public void Remove(string key) => _store.Remove(key);
            public void Set(string key, byte[] value) => _store[key] = value;
            public bool TryGetValue(string key, out byte[] value)
            {
                var found = _store.TryGetValue(key, out var v);
                value = v!;
                return found;
            }
        }

        /// <summary>
        /// 造一个真内存 IFormFile（FormFile 真实 CopyToAsync，不做 Mock）。
        /// </summary>
        private static IFormFile MakeFile(string fieldName, string content = "hello xhd")
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, fieldName, fieldName);
        }

        /// <summary>
        /// 全公司数据权限 + 按钮权限放行（两件套）。
        /// </summary>
        private static Mock<IDBAuthService> CreateFullAccessAuth()
        {
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 4, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            return auth;
        }

        /// <summary>
        /// Mock ICRM_Customer_attaService：桥接到真实仓储；addFailure=true 时 AddAsync 返回 0
        /// 以覆盖 "文件保存失败"/"保存失败" 分支。
        /// </summary>
        private Mock<ICRM_Customer_attaService> CreateServiceMock(bool addFailure = false,
            bool deleteFailure = false)
        {
            var mock = new Mock<ICRM_Customer_attaService>();
            mock.Setup(s => s.AddAsync(It.IsAny<CRM_Customer_atta>()))
                .Returns((CRM_Customer_atta m) => addFailure
                    ? Task.FromResult(0)
                    : _repo.AddAsync(m));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer_atta, bool>>>()))
                .Returns((Expression<Func<CRM_Customer_atta, bool>> e) => _repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<CRM_Customer_atta, bool>>>(),
                                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<CRM_Customer_atta, bool>> e, int p, int l, string ob)
                    => _repo.GridAsync(e, p, l, ob));
            mock.Setup(s => s.DeleteAsync(It.IsAny<Expression<Func<CRM_Customer_atta, bool>>>()))
                .Returns((Expression<Func<CRM_Customer_atta, bool>> e) => deleteFailure
                    ? Task.FromResult(0)
                    : _repo.DeleteAsync(e));
            return mock;
        }

        private static CustomerAttaController CreateController(
            ICRM_Customer_attaService detailService,
            string customerId = "C1",
            Dictionary<string, StringValues>? formFields = null,
            FormFileCollection? files = null,
            string userId = "TEST_USER",
            string queryString = "")
        {
            var ctrl = new CustomerAttaController(
                new Mock<ILogger<CustomerAttaController>>().Object,
                new Mock<ICRM_CustomerService>().Object,
                detailService,
                CreateFullAccessAuth().Object);

            var httpCtx = new DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = IPAddress.Loopback;

            // Upload:96 三个装配依赖：ClaimsIdentity(含 "uid") + Session + Request.Form
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim("uid", "testuid")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

            var session = new DictSession();
            session.SetString("newCustomerID", customerId);
            httpCtx.Session = session;

            if (!string.IsNullOrEmpty(queryString))
            {
                httpCtx.Request.QueryString = new QueryString("?" + queryString.TrimStart('?'));
            }

            httpCtx.Request.Form = new FormCollection(
                formFields ?? new Dictionary<string, StringValues>(),
                files ?? new FormFileCollection());

            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        private static string UploadRoot => Path.Combine(
            Directory.GetCurrentDirectory(), "wwwroot", "upload", "customer");

        // ============ 普通上传 ============

        [Fact]
        public async Task Upload_Normal_WritesFileAndDbRecord()
        {
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues>
            {
                { "name", "report.txt" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var dir = Path.Combine(UploadRoot, "C1");
            Assert.True(Directory.Exists(dir));
            var savedFiles = Directory.GetFiles(dir);
            Assert.Single(savedFiles);
            Assert.Equal("hello xhd", File.ReadAllText(savedFiles[0]));

            var rows = await _fsql.Select<CRM_Customer_atta>().Where(a => a.cus_id == "C1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("report.txt", rows[0].file_name);
            Assert.EndsWith(".txt", rows[0].real_name);
            Assert.Equal("C1", rows[0].cus_id);
            Assert.Equal("TEST_USER", rows[0].create_id);
            Assert.NotNull(rows[0].create_time);
        }

        [Fact]
        public async Task Upload_Normal_CustomerIdTraversal_ReturnsIllegalParam()
        {
            // Path.GetFileName("..") == ".."，触发参数非法分支
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues> { { "name", "a.txt" } };
            var ctrl = CreateController(CreateServiceMock().Object, "..", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数非法", (string)obj["msg"]!);
            Assert.False(Directory.Exists(UploadRoot));
        }

        [Fact]
        public async Task Upload_Normal_FileNameTraversal_ReturnsIllegalParam()
        {
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues> { { "name", ".." } };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数非法", (string)obj["msg"]!);
            // 参数非法时不应落库
            Assert.Equal(0, await _fsql.Select<CRM_Customer_atta>().CountAsync());
        }

        [Fact]
        public async Task Upload_Normal_EmptyFile_SkipsAndReturnsSuccess()
        {
            var emptyBytes = Array.Empty<byte>();
            var file = new FormFile(new MemoryStream(emptyBytes), 0, 0, "file", "file");
            var files = new FormFileCollection { file };
            var fields = new Dictionary<string, StringValues> { { "name", "empty.txt" } };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.False(Directory.Exists(Path.Combine(UploadRoot, "C1")));
            Assert.Equal(0, await _fsql.Select<CRM_Customer_atta>().CountAsync());
        }

        [Fact]
        public async Task Upload_Normal_AddFails_ReturnsSaveFailedError()
        {
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues> { { "name", "a.txt" } };
            var ctrl = CreateController(CreateServiceMock(addFailure: true).Object, "C1", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("文件保存失败", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Upload_Normal_MultipleFiles_WritesAll()
        {
            var files = new FormFileCollection
            {
                MakeFile("f1", "content-one"),
                MakeFile("f2", "content-two")
            };
            var fields = new Dictionary<string, StringValues> { { "name", "a.txt" } };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(UploadRoot, "C1")).Length);
            Assert.Equal(2, await _fsql.Select<CRM_Customer_atta>().Where(a => a.cus_id == "C1").CountAsync());
        }

        // ============ 分片上传 ============

        [Fact]
        public async Task Upload_Chunk_WritesChunkToTempDir()
        {
            var files = new FormFileCollection { MakeFile("file", "chunk-one") };
            var fields = new Dictionary<string, StringValues>
            {
                { "chunks", "1" },
                { "guid", "G1" },
                { "id", "A1" },
                { "chunk", "1" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var chunkPath = Path.Combine(UploadRoot, "G1-A1", "1");
            Assert.True(File.Exists(chunkPath));
            Assert.Equal("chunk-one", File.ReadAllText(chunkPath));
            // 分片分支不写业务表
            Assert.Equal(0, await _fsql.Select<CRM_Customer_atta>().CountAsync());
        }

        [Fact]
        public async Task Upload_Chunk_GuidTraversal_ReturnsIllegalParam()
        {
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues>
            {
                { "chunks", "1" },
                { "guid", ".." },
                { "id", "A1" },
                { "chunk", "1" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数非法", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Upload_Chunk_AttIdTraversal_ReturnsIllegalParam()
        {
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues>
            {
                { "chunks", "1" },
                { "guid", "G1" },
                { "id", ".." },
                { "chunk", "1" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数非法", (string)obj["msg"]!);
        }

        [Fact]
        public async Task Upload_Chunk_MultipleRequests_AccumulateInTempDir()
        {
            var baseFields = new Dictionary<string, StringValues>
            {
                { "chunks", "1" },
                { "guid", "G2" },
                { "id", "A2" }
            };

            // 两次请求分别上传第 1、2 片（顺序按文件名长度+名称排序）
            foreach (var chunk in new[] { "1", "2" })
            {
                var files = new FormFileCollection { MakeFile("file", "part-" + chunk) };
                var fields = new Dictionary<string, StringValues>(baseFields) { { "chunk", chunk } };
                var ctrl = CreateController(CreateServiceMock().Object, "C1", fields, files);

                var json = await ctrl.Upload();
                var obj = JObject.Parse(json);
                Assert.Equal(0, (int)obj["code"]!);
            }

            var dir = Path.Combine(UploadRoot, "G2-A2");
            Assert.Equal(2, Directory.GetFiles(dir).Length);
            Assert.Equal("part-1", File.ReadAllText(Path.Combine(dir, "1")));
            Assert.Equal("part-2", File.ReadAllText(Path.Combine(dir, "2")));
        }

        // ============ Meger 合并 ============

        [Fact]
        public async Task Meger_TwoChunks_MergesFileAndDeletesChunksAndDbRow()
        {
            var customerId = "C1";
            // 预置 2 个已落盘的分片
            var tempDir = Path.Combine(UploadRoot, "G3-A3");
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "1"), "AAA");
            File.WriteAllText(Path.Combine(tempDir, "2"), "BBB");

            var fields = new Dictionary<string, StringValues>
            {
                { "guid", "G3" },
                { "id", "A3" },
                { "name", "merged.pdf" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, customerId, fields);

            var json = await ctrl.Meger();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            // 合并产物落在客户目录
            var saveDir = Path.Combine(UploadRoot, customerId);
            var merged = Directory.GetFiles(saveDir);
            Assert.Single(merged);
            Assert.EndsWith(".pdf", merged[0]);
            Assert.Equal("AAABBB", File.ReadAllText(merged[0]));

            // 分片目录被清理
            Assert.False(Directory.Exists(tempDir));

            // CRM_Customer_atta 落库
            var rows = await _fsql.Select<CRM_Customer_atta>().Where(a => a.cus_id == customerId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal("merged.pdf", rows[0].file_name);
            Assert.EndsWith(".pdf", rows[0].real_name);
            Assert.Equal(customerId, rows[0].cus_id);
            Assert.Equal("TEST_USER", rows[0].create_id);
        }

        [Fact]
        public async Task Meger_NoTempDir_ReturnsSuccessWithoutDbRow()
        {
            // 无分片临时目录：Meger 不应抛异常（降级返回成功），也不落库
            var fields = new Dictionary<string, StringValues>
            {
                { "guid", "G4" },
                { "id", "A4" },
                { "name", "x.txt" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, "C9", fields);

            var json = await ctrl.Meger();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, await _fsql.Select<CRM_Customer_atta>().CountAsync());
        }

        [Fact]
        public async Task Meger_CustomerIdTraversal_ReturnsIllegalParam()
        {
            var fields = new Dictionary<string, StringValues>
            {
                { "guid", "G5" },
                { "id", "A5" },
                { "name", "x.txt" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, "..", fields);

            var json = await ctrl.Meger();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数非法", (string)obj["msg"]!);
        }

        // ============ Save ============

        [Fact]
        public async Task Save_ValidForm_InsertsRow()
        {
            var fields = new Dictionary<string, StringValues>
            {
                { "id", "S1" },
                { "cus_id", "C1" },
                { "file_name", "doc.docx" },
                { "real_name", "2024.docx" },
                { "file_size", "10" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields);

            var json = await ctrl.Save();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<CRM_Customer_atta>().Where(a => a.id == "S1").FirstAsync();
            Assert.NotNull(row);
            Assert.Equal("C1", row.cus_id);
            Assert.Equal("doc.docx", row.file_name);
            Assert.Equal("2024.docx", row.real_name);
            Assert.Equal(10, row.file_size);
            Assert.Equal("TEST_USER", row.create_id);
        }

        [Fact]
        public async Task Save_NonNumericFileSize_ParsedAsZero()
        {
            var fields = new Dictionary<string, StringValues>
            {
                { "id", "S2" },
                { "cus_id", "C1" },
                { "file_name", "doc.docx" },
                { "real_name", "2024.docx" },
                { "file_size", "not-a-number" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields);

            var json = await ctrl.Save();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var row = await _fsql.Select<CRM_Customer_atta>().Where(a => a.id == "S2").FirstAsync();
            Assert.NotNull(row);
            Assert.Equal(0, row.file_size);
        }

        [Fact]
        public async Task Save_AddFails_ReturnsSaveFailedError()
        {
            var fields = new Dictionary<string, StringValues>
            {
                { "id", "S3" },
                { "cus_id", "C1" },
                { "file_name", "a.txt" },
                { "real_name", "a.txt" },
                { "file_size", "1" }
            };
            var ctrl = CreateController(CreateServiceMock(addFailure: true).Object, "C1", fields);

            var json = await ctrl.Save();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("保存失败", (string)obj["msg"]!);
        }

        // ============ Del ============

        [Fact]
        public async Task Del_ExistingRecord_DeletesRowAndPhysicalFile()
        {
            // 落库 + 落盘
            await _fsql.Insert(new CRM_Customer_atta
            {
                id = "D1",
                cus_id = "C1",
                file_name = "del.txt",
                real_name = "del-001.txt",
                create_id = "TEST_USER",
                create_time = DateTime.Now
            }).ExecuteAffrowsAsync();

            var saveDir = Path.Combine(UploadRoot, "C1");
            Directory.CreateDirectory(saveDir);
            var physicalPath = Path.Combine(saveDir, "del-001.txt");
            File.WriteAllText(physicalPath, "to-be-deleted");

            var fields = new Dictionary<string, StringValues> { { "cus_id", "C1" } };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields);

            var json = await ctrl.Del("D1");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, await _fsql.Select<CRM_Customer_atta>().Where(a => a.id == "D1").CountAsync());
            Assert.False(File.Exists(physicalPath));
        }

        [Fact]
        public async Task Del_MissingPhysicalFile_StillReturnsSuccess()
        {
            await _fsql.Insert(new CRM_Customer_atta
            {
                id = "D2",
                cus_id = "C1",
                file_name = "ghost.txt",
                real_name = "ghost.txt",
                create_id = "TEST_USER",
                create_time = DateTime.Now
            }).ExecuteAffrowsAsync();

            // 不创建物理文件：File.Delete 异常被吞，仍返回成功
            var fields = new Dictionary<string, StringValues> { { "cus_id", "C1" } };
            var ctrl = CreateController(CreateServiceMock().Object, "C1", fields);

            var json = await ctrl.Del("D2");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, await _fsql.Select<CRM_Customer_atta>().Where(a => a.id == "D2").CountAsync());
        }

        [Fact]
        public async Task Del_RepositoryDeleteFails_ReturnsDeleteFailedError()
        {
            await _fsql.Insert(new CRM_Customer_atta
            {
                id = "D3",
                cus_id = "C1",
                file_name = "x.txt",
                real_name = "x.txt",
                create_id = "TEST_USER",
                create_time = DateTime.Now
            }).ExecuteAffrowsAsync();

            var fields = new Dictionary<string, StringValues> { { "cus_id", "C1" } };
            var ctrl = CreateController(CreateServiceMock(deleteFailure: true).Object, "C1", fields);

            var json = await ctrl.Del("D3");
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("删除失败", (string)obj["msg"]!);
        }

        // ============ Grid ============

        [Fact]
        public async Task Grid_FiltersByCustomerId_AndDoesNotLeakAcrossCustomers()
        {
            var custA = "11111111-1111-1111-1111-111111111111";
            var custB = "22222222-2222-2222-2222-222222222222";

            await _fsql.Insert(new CRM_Customer_atta
            {
                id = "G1", cus_id = custA, file_name = "a.txt", real_name = "a.txt",
                create_id = "TEST_USER", create_time = DateTime.Now
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new CRM_Customer_atta
            {
                id = "G2", cus_id = custA, file_name = "b.txt", real_name = "b.txt",
                create_id = "TEST_USER", create_time = DateTime.Now
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new CRM_Customer_atta
            {
                id = "G3", cus_id = custB, file_name = "c.txt", real_name = "c.txt",
                create_id = "OTHER_USER", create_time = DateTime.Now
            }).ExecuteAffrowsAsync();

            // PageValidate.checkID 要求 GUID 格式
            var ctrl = CreateController(CreateServiceMock().Object, custA, queryString: "?id=" + custA);

            var json = await ctrl.Grid(new PageView<CRM_Customer_atta> { Page = 1, Limit = 30 });
            var data = JObject.Parse(json);

            Assert.Equal(0, (int)data["code"]!);
            Assert.Equal(2, (long)data["count"]!);
            var rows = (JArray)data["data"]!;
            Assert.All(rows, r => Assert.Equal(custA, (string)r["cus_id"]!));
        }

        [Fact]
        public async Task Grid_RestrictedAuthWithEmptyEmpList_ReturnsNoPermission()
        {
            // authtype != 4 且 empList 为空 → 无权限
            var auth = new Mock<IDBAuthService>();
            auth.Setup(a => a.GetDataAuth(It.IsAny<string>()))
                .ReturnsAsync(new XHDRoleData { authtype = 2, empList = new List<string>() });
            auth.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            var ctrl = new CustomerAttaController(
                new Mock<ILogger<CustomerAttaController>>().Object,
                new Mock<ICRM_CustomerService>().Object,
                CreateServiceMock().Object,
                auth.Object);
            var httpCtx = new DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = IPAddress.Loopback;
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Sid, "TEST_USER"),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim("uid", "testuid")
            }, "TestAuth"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };

            var json = await ctrl.Grid(new PageView<CRM_Customer_atta> { Page = 1, Limit = 30 });
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("无权限", (string)obj["msg"]!);
        }
    }
}
