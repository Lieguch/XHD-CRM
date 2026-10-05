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
    /// Sprint 10.38 难点线：SaleContractAttaController 合同附件上传真测试。
    /// 结构与 CustomerAttaUploadTests 同构（构造只有 3 个依赖，无 IDBAuthService、无 Session），
    /// 路径根目录 {cwd}/wwwroot/upload/contract/。覆盖普通上传、分片上传（Form key 为 "chunk"）、
    /// Meger 合并、Del（删记录 + 删物理文件）、Grid（按 contract_id 过滤）。
    /// 全部走真实 SQLite 内存库 + 真实 Sale_contract_attaRepository + 真实文件 IO（隔离在临时目录）。
    /// </summary>
    [Collection("XhdFileSystem")]
    public class SaleContractAttaUploadTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly Sale_contract_attaRepository _repo;
        private readonly string _origDir;
        private readonly string _tmpDir;

        public SaleContractAttaUploadTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _repo = new Sale_contract_attaRepository(_fsql);

            _origDir = Directory.GetCurrentDirectory();
            _tmpDir = Path.Combine(Path.GetTempPath(), "xhdcontract_" + Guid.NewGuid().ToString("N"));
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

        private static IFormFile MakeFile(string fieldName, string content = "hello contract")
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, fieldName, fieldName);
        }

        private Mock<ISale_contract_attaService> CreateServiceMock(bool addFailure = false)
        {
            var mock = new Mock<ISale_contract_attaService>();
            mock.Setup(s => s.AddAsync(It.IsAny<Sale_contract_atta>()))
                .Returns((Sale_contract_atta m) => addFailure
                    ? Task.FromResult(0)
                    : _repo.AddAsync(m));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sale_contract_atta, bool>>>()))
                .Returns((Expression<Func<Sale_contract_atta, bool>> e) => _repo.GridAsync(e, 1, 100000));
            mock.Setup(s => s.GridAsync(It.IsAny<Expression<Func<Sale_contract_atta, bool>>>(),
                                        It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()))
                .Returns((Expression<Func<Sale_contract_atta, bool>> e, int p, int l, string ob)
                    => _repo.GridAsync(e, p, l, ob));
            mock.Setup(s => s.DeleteAsync(It.IsAny<Expression<Func<Sale_contract_atta, bool>>>()))
                .Returns((Expression<Func<Sale_contract_atta, bool>> e) => _repo.DeleteAsync(e));
            return mock;
        }

        private static SaleContractAttaController CreateController(
            ISale_contract_attaService detailService,
            Dictionary<string, StringValues>? formFields = null,
            FormFileCollection? files = null,
            string userId = "TEST_USER",
            string queryString = "")
        {
            var ctrl = new SaleContractAttaController(
                new Mock<ILogger<SaleContractAttaController>>().Object,
                new Mock<ISale_contractService>().Object,
                detailService);

            var httpCtx = new DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = IPAddress.Loopback;

            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim("uid", "testuid")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

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
            Directory.GetCurrentDirectory(), "wwwroot", "upload", "contract");

        // ============ 普通上传 ============

        [Fact]
        public async Task Upload_Normal_WritesFileAndDbRecord()
        {
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues>
            {
                { "contract_id", "K1" },
                { "name", "contract.pdf" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var dir = Path.Combine(UploadRoot, "K1");
            Assert.True(Directory.Exists(dir));
            var savedFiles = Directory.GetFiles(dir);
            Assert.Single(savedFiles);
            Assert.Equal("hello contract", File.ReadAllText(savedFiles[0]));

            var rows = await _fsql.Select<Sale_contract_atta>().Where(a => a.contract_id == "K1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("contract.pdf", rows[0].file_name);
            Assert.EndsWith(".pdf", rows[0].real_name);
            Assert.Equal("K1", rows[0].contract_id);
            Assert.Equal("TEST_USER", rows[0].create_id);
            Assert.NotNull(rows[0].create_time);
        }

        [Fact]
        public async Task Upload_Normal_ContractIdTraversal_ReturnsIllegalParam()
        {
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues>
            {
                { "contract_id", ".." },
                { "name", "a.pdf" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields, files);

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
            var fields = new Dictionary<string, StringValues>
            {
                { "contract_id", "K1" },
                { "name", ".." }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数非法", (string)obj["msg"]!);
            Assert.Equal(0, await _fsql.Select<Sale_contract_atta>().CountAsync());
        }

        [Fact]
        public async Task Upload_Normal_EmptyFile_SkipsAndReturnsSuccess()
        {
            var emptyBytes = Array.Empty<byte>();
            var files = new FormFileCollection
            {
                new FormFile(new MemoryStream(emptyBytes), 0, 0, "file", "file")
            };
            var fields = new Dictionary<string, StringValues>
            {
                { "contract_id", "K1" },
                { "name", "empty.pdf" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.False(Directory.Exists(Path.Combine(UploadRoot, "K1")));
            Assert.Equal(0, await _fsql.Select<Sale_contract_atta>().CountAsync());
        }

        // ============ 分片上传 ============

        [Fact]
        public async Task Upload_Chunk_WritesChunkToTempDir()
        {
            var files = new FormFileCollection { MakeFile("file", "chunk-one") };
            var fields = new Dictionary<string, StringValues>
            {
                { "chunk", "1" },
                { "guid", "G1" },
                { "id", "A1" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var chunkPath = Path.Combine(UploadRoot, "G1-A1", "1");
            Assert.True(File.Exists(chunkPath));
            Assert.Equal("chunk-one", File.ReadAllText(chunkPath));
            Assert.Equal(0, await _fsql.Select<Sale_contract_atta>().CountAsync());
        }

        [Fact]
        public async Task Upload_Chunk_GuidTraversal_ReturnsIllegalParam()
        {
            var files = new FormFileCollection { MakeFile("file") };
            var fields = new Dictionary<string, StringValues>
            {
                { "chunk", "1" },
                { "guid", ".." },
                { "id", "A1" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields, files);

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
                { "chunk", "1" },
                { "guid", "G1" },
                { "id", ".." }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields, files);

            var json = await ctrl.Upload();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数非法", (string)obj["msg"]!);
        }

        // ============ Meger 合并 ============

        [Fact]
        public async Task Meger_TwoChunks_MergesFileAndDeletesChunksAndDbRow()
        {
            var tempDir = Path.Combine(UploadRoot, "G2-A2");
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "1"), "CCC");
            File.WriteAllText(Path.Combine(tempDir, "2"), "DDD");

            var fields = new Dictionary<string, StringValues>
            {
                { "contract_id", "K1" },
                { "guid", "G2" },
                { "id", "A2" },
                { "name", "merged.pdf" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields);

            var json = await ctrl.Meger();
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);

            var saveDir = Path.Combine(UploadRoot, "K1");
            var merged = Directory.GetFiles(saveDir);
            Assert.Single(merged);
            Assert.EndsWith(".pdf", merged[0]);
            Assert.Equal("CCCDDD", File.ReadAllText(merged[0]));

            Assert.False(Directory.Exists(tempDir));

            var rows = await _fsql.Select<Sale_contract_atta>().Where(a => a.contract_id == "K1").ToListAsync();
            Assert.Single(rows);
            Assert.Equal("merged.pdf", rows[0].file_name);
            Assert.EndsWith(".pdf", rows[0].real_name);
            Assert.Equal("TEST_USER", rows[0].create_id);
        }

        [Fact]
        public async Task Meger_ContractIdTraversal_ReturnsIllegalParam()
        {
            var fields = new Dictionary<string, StringValues>
            {
                { "contract_id", ".." },
                { "guid", "G3" },
                { "id", "A3" },
                { "name", "x.pdf" }
            };
            var ctrl = CreateController(CreateServiceMock().Object, fields);

            var json = await ctrl.Meger();
            var obj = JObject.Parse(json);

            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Equal("参数非法", (string)obj["msg"]!);
        }

        // ============ Del ============

        [Fact]
        public async Task Del_ExistingRecord_DeletesRowAndPhysicalFile()
        {
            await _fsql.Insert(new Sale_contract_atta
            {
                id = "D1",
                contract_id = "K1",
                file_name = "del.pdf",
                real_name = "del-001.pdf",
                create_id = "TEST_USER",
                create_time = DateTime.Now
            }).ExecuteAffrowsAsync();

            var saveDir = Path.Combine(UploadRoot, "K1");
            Directory.CreateDirectory(saveDir);
            var physicalPath = Path.Combine(saveDir, "del-001.pdf");
            File.WriteAllText(physicalPath, "to-be-deleted");

            var fields = new Dictionary<string, StringValues> { { "contract_id", "K1" } };
            var ctrl = CreateController(CreateServiceMock().Object, fields);

            var json = await ctrl.Del("D1");
            var obj = JObject.Parse(json);

            Assert.Equal(0, (int)obj["code"]!);
            Assert.Equal(0, await _fsql.Select<Sale_contract_atta>().Where(a => a.id == "D1").CountAsync());
            Assert.False(File.Exists(physicalPath));
        }

        // ============ Grid ============

        [Fact]
        public async Task Grid_FiltersByContractId_AndDoesNotLeakAcrossContracts()
        {
            await _fsql.Insert(new Sale_contract_atta
            {
                id = "R1", contract_id = "K1", file_name = "a.pdf", real_name = "a.pdf",
                create_id = "TEST_USER", create_time = DateTime.Now
            }).ExecuteAffrowsAsync();
            await _fsql.Insert(new Sale_contract_atta
            {
                id = "R2", contract_id = "K2", file_name = "b.pdf", real_name = "b.pdf",
                create_id = "TEST_USER", create_time = DateTime.Now
            }).ExecuteAffrowsAsync();

            var ctrl = CreateController(CreateServiceMock().Object, queryString: "?id=K1");

            var json = await ctrl.Grid(new PageView<Sale_contract_atta> { Page = 1, Limit = 30 });
            var data = JObject.Parse(json);

            Assert.Equal(0, (int)data["code"]!);
            Assert.Equal(1, (long)data["count"]!);
            var rows = (JArray)data["data"]!;
            Assert.Single(rows);
            Assert.Equal("K1", (string)rows[0]["contract_id"]!);
        }
    }
}
