using FreeSql;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.IRepository;
using XHD.Core.IServices;
using XHD.Core.Models;
using XHD.Core.Repository;
using XHD.Core.View.Controllers;
using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// Sprint 5 Wave 1 单元测试：岗位/员工 8 函数。
    ///   #27  HrPostController.UpdatePost（变更员工岗位三元组）
    ///   #31/#81 HrPostController.GetRole（合并 BLL + Server 层）
    ///   #34  HrPostController.UpdatePostEmp
    ///   #35  HrPostController.UpdatePostEmpbyEid
    ///   #88  HrPostController.GetPostByEmpId
    ///   #89  HrPostController.Serch
    ///   #90  HrPostController.Postemp
    /// 大部分测试直接打 Repository + SQLite，绕开 Controller 装配；
    /// 少数 Controller 级测试用 Moq 桥接服务（对齐 Sprint 4 模式）。
    /// </summary>
    public class HrPostEmployeeTests : IDisposable
    {
        private readonly IFreeSql _fsql;
        private readonly hr_postRepository _postRepo;
        private readonly Sys_role_empRepository _roleEmpRepo;
        private readonly hr_employeeRepository _empRepo;
        private readonly Sys_roleRepository _roleRepo;

        public HrPostEmployeeTests()
        {
            _fsql = TestDbContextFactory.CreateFreeSql();
            _postRepo = new hr_postRepository(_fsql);
            _roleEmpRepo = new Sys_role_empRepository(_fsql);
            _empRepo = new hr_employeeRepository(_fsql);
            _roleRepo = new Sys_roleRepository(_fsql);
        }

        public void Dispose()
        {
            _fsql?.Dispose();
        }

        // ============ 测试数据工厂 ============

        private static hr_post NewPost(string id, string name, string empId = "", int? defaultPost = 0,
            string depId = "DEP1", string positionId = "POS1")
        {
            return new hr_post
            {
                id = id,
                post_name = name,
                position_id = positionId,
                dep_id = depId,
                emp_id = empId,
                default_post = defaultPost,
                create_time = new DateTime(2024, 3, 1)
            };
        }

        private static hr_employee NewEmp(string id, string uid = "u1", string name = "张三",
            string depId = "DEP1", string postId = "POST1", string positionId = "POS1")
        {
            return new hr_employee
            {
                id = id,
                uid = uid,
                name = name,
                dep_id = depId,
                post_id = postId,
                position_id = positionId,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private static Sys_role NewRole(string id, string name = "管理员")
        {
            return new Sys_role
            {
                id = id,
                RoleName = name,
                create_time = new DateTime(2024, 1, 1)
            };
        }

        private static Sys_role_emp NewRoleEmp(string roleId, string empId)
        {
            return new Sys_role_emp
            {
                id = Guid.NewGuid().ToString(),
                role_id = roleId,
                emp_id = empId
            };
        }

        // ============ #27 UpdatePost：变更员工岗位 ============

        [Fact]
        public async Task UpdatePost_HappyPath_UpdatesTripleFields()
        {
            var empId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewEmp(empId)).ExecuteAffrowsAsync();

            var ok = await _empRepo.UpdatePostAsync(empId, "DEP2", "POST2", "POS2");

            Assert.True(ok);
            var emp = await _fsql.Select<hr_employee>().Where(a => a.id == empId).FirstAsync();
            Assert.Equal("DEP2", emp.dep_id);
            Assert.Equal("POST2", emp.post_id);
            Assert.Equal("POS2", emp.position_id);
        }

        [Fact]
        public async Task UpdatePost_NonExistentId_ReturnsFalse()
        {
            var ok = await _empRepo.UpdatePostAsync(
                Guid.NewGuid().ToString(), "DEP", "POST", "POS");
            Assert.False(ok);
        }

        [Fact]
        public async Task UpdatePost_EmptyEmpId_ReturnsFalse()
        {
            var ok = await _empRepo.UpdatePostAsync("", "DEP", "POST", "POS");
            Assert.False(ok);
        }

        // ============ #34 UpdatePostEmp ============

        [Fact]
        public async Task UpdatePostEmp_HappyPath_UpdatesFields()
        {
            var postId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewPost(postId, "销售经理")).ExecuteAffrowsAsync();

            var ok = await _postRepo.UpdatePostEmpAsync(postId, "EMP1", 1);

            Assert.True(ok);
            var p = await _fsql.Select<hr_post>().Where(a => a.id == postId).FirstAsync();
            Assert.Equal("EMP1", p.emp_id);
            Assert.Equal(1, p.default_post);
        }

        [Fact]
        public async Task UpdatePostEmp_NonExistentId_ReturnsFalse()
        {
            var ok = await _postRepo.UpdatePostEmpAsync(
                Guid.NewGuid().ToString(), "EMP1", 0);
            Assert.False(ok);
        }

        // ============ #35 UpdatePostEmpbyEid ============

        [Fact]
        public async Task UpdatePostEmpbyEid_HappyPath_ClearsAllPosts()
        {
            // Arrange：某员工名下有两个岗位
            var postId1 = Guid.NewGuid().ToString();
            var postId2 = Guid.NewGuid().ToString();
            await _fsql.Insert(new List<hr_post>
            {
                NewPost(postId1, "岗位A", empId: "EMP1", defaultPost: 1),
                NewPost(postId2, "岗位B", empId: "EMP1", defaultPost: 0)
            }).ExecuteAffrowsAsync();

            var ok = await _postRepo.UpdatePostEmpbyEidAsync("EMP1");

            Assert.True(ok);
            var rows = await _fsql.Select<hr_post>()
                .Where(a => a.id == postId1 || a.id == postId2)
                .ToListAsync();
            Assert.Equal(2, rows.Count);
            foreach (var r in rows)
            {
                Assert.Equal(string.Empty, r.emp_id);
                Assert.Equal(0, r.default_post);
            }
        }

        [Fact]
        public async Task UpdatePostEmpbyEid_NoMatchingPosts_ReturnsFalse()
        {
            var ok = await _postRepo.UpdatePostEmpbyEidAsync("NOSUCHEMP");
            Assert.False(ok);
        }

        // ============ #88 GetPostByEmpId ============

        [Fact]
        public async Task GetPostByEmpId_HappyPath_ReturnsPosts()
        {
            var postId1 = Guid.NewGuid().ToString();
            var postId2 = Guid.NewGuid().ToString();
            var postId3 = Guid.NewGuid().ToString();
            await _fsql.Insert(new List<hr_post>
            {
                NewPost(postId1, "岗位A", empId: "EMP1", defaultPost: 0),
                NewPost(postId2, "岗位B", empId: "EMP1", defaultPost: 1),
                NewPost(postId3, "岗位C", empId: "OTHER", defaultPost: 0)
            }).ExecuteAffrowsAsync();

            var posts = await _postRepo.GetPostByEmpIdAsync("EMP1");

            Assert.Equal(2, posts.Count);
            var ids = posts.Select(p => p.id).ToHashSet();
            Assert.Contains(postId1, ids);
            Assert.Contains(postId2, ids);
        }

        [Fact]
        public async Task GetPostByEmpId_EmptySearchText_ReturnsEmpty()
        {
            var posts = await _postRepo.GetPostByEmpIdAsync("");
            Assert.Empty(posts);
        }

        // ============ #89 Serch ============

        [Fact]
        public async Task Serch_HappyPath_FindsMatching()
        {
            await _fsql.Insert(new List<hr_post>
            {
                NewPost(Guid.NewGuid().ToString(), "销售经理"),
                NewPost(Guid.NewGuid().ToString(), "销售总监"),
                NewPost(Guid.NewGuid().ToString(), "产品经理")
            }).ExecuteAffrowsAsync();

            var posts = await _postRepo.SerchAsync("销售");

            Assert.Equal(2, posts.Count);
            Assert.All(posts, p => Assert.Contains("销售", p.post_name));
        }

        [Fact]
        public async Task Serch_NoMatch_ReturnsEmpty()
        {
            await _fsql.Insert(NewPost(Guid.NewGuid().ToString(), "销售经理")).ExecuteAffrowsAsync();

            var posts = await _postRepo.SerchAsync("财务");

            Assert.Empty(posts);
        }

        [Fact]
        public async Task Serch_EmptySearchText_ReturnsEmpty()
        {
            var posts = await _postRepo.SerchAsync("   ");
            Assert.Empty(posts);
        }

        // ============ #31/#81 GetRolesByEmpId ============

        [Fact]
        public async Task GetRoleIdsByEmpId_HappyPath_ReturnsLinkedRoleIds()
        {
            var roleId1 = Guid.NewGuid().ToString();
            var roleId2 = Guid.NewGuid().ToString();
            var roleId3 = Guid.NewGuid().ToString();

            await _fsql.Insert(new List<Sys_role>
            {
                NewRole(roleId1, "管理员"),
                NewRole(roleId2, "普通员工"),
                NewRole(roleId3, "客服")
            }).ExecuteAffrowsAsync();

            await _fsql.Insert(new List<Sys_role_emp>
            {
                NewRoleEmp(roleId1, "EMP1"),
                NewRoleEmp(roleId2, "EMP1"),
                NewRoleEmp(roleId3, "OTHER")
            }).ExecuteAffrowsAsync();

            var ids = await _roleEmpRepo.GetRoleIdsByEmpIdAsync("EMP1");

            Assert.Equal(2, ids.Count);
            var set = ids.ToHashSet();
            Assert.Contains(roleId1, set);
            Assert.Contains(roleId2, set);
            Assert.DoesNotContain(roleId3, set);

            // 端到端：按 roleIds 反查 Sys_role 得到 2 条
            var roles = await _fsql.Select<Sys_role>().Where(a => ids.Contains(a.id)).ToListAsync();
            Assert.Equal(2, roles.Count);
        }

        [Fact]
        public async Task GetRoleIdsByEmpId_NoLinks_ReturnsEmpty()
        {
            var ids = await _roleEmpRepo.GetRoleIdsByEmpIdAsync("NOSUCHEMP");
            Assert.Empty(ids);
        }

        // ============ #90 Postemp（Controller 集成测试）============

        private HrPostController CreatePostController(bool grantEdit = true, string userId = "TEST_USER")
        {
            var postSvc = new Mock<Ihr_postService>();
            postSvc.Setup(s => s.UpdatePostEmpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
                .ReturnsAsync(true);

            var empSvc = new Mock<Ihr_employeeService>();
            empSvc.Setup(s => s.UpdatePostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var roleEmpSvc = new Mock<ISys_role_empService>();

            var authMock = new Mock<IDBAuthService>();
            authMock.Setup(a => a.GetAuth(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(grantEdit);

            var ctrl = new HrPostController(
                new Mock<ILogger<HrPostController>>().Object,
                postSvc.Object,
                empSvc.Object,
                roleEmpSvc.Object,
                new Mock<ISys_logService>().Object,
                authMock.Object);

            var httpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            httpCtx.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            var claims = new[]
            {
                new Claim(ClaimTypes.Sid, userId),
                new Claim(ClaimTypes.Name, "Test User")
            };
            httpCtx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            ctrl.ControllerContext = new ControllerContext { HttpContext = httpCtx };
            return ctrl;
        }

        [Fact]
        public async Task Postemp_HappyPath_UpdatesPostsAndEmployee()
        {
            var ctrl = CreatePostController(grantEdit: true);

            var postdata = new PostData[]
            {
                new PostData { Post_id = "P1", Default_post = 0, Dep_id = "D1", Position_id = "POS1" },
                new PostData { Post_id = "P2", Default_post = 1, Dep_id = "D2", Position_id = "POS2" }
            };

            var json = await ctrl.Postemp("EMP1", postdata);
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
        }

        [Fact]
        public async Task Postemp_NoPermission_ReturnsError()
        {
            var ctrl = CreatePostController(grantEdit: false);

            var postdata = new PostData[]
            {
                new PostData { Post_id = "P1", Default_post = 1, Dep_id = "D1", Position_id = "POS1" }
            };

            var json = await ctrl.Postemp("EMP1", postdata);
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
            Assert.Contains("无权限", (string)obj["msg"]!);
        }

        [Fact]
        public async Task UpdatePost_Controller_HappyPath_ReturnsSuccess()
        {
            var ctrl = CreatePostController(grantEdit: true);
            var json = await ctrl.UpdatePost("EMP1", "DEP2", "POST2", "POS2");
            var obj = JObject.Parse(json);
            Assert.Equal(0, (int)obj["code"]!);
            Assert.Contains("更新成功", (string)obj["msg"]!);
        }

        [Fact]
        public async Task UpdatePost_Controller_InvalidId_ReturnsError()
        {
            var ctrl = CreatePostController();
            var json = await ctrl.UpdatePost("", "DEP", "POST", "POS");
            var obj = JObject.Parse(json);
            Assert.Equal(-1, (int)obj["code"]!);
        }

        [Fact]
        public async Task GetRole_Controller_EmptyEmpid_ReturnsEmptyObject()
        {
            var ctrl = CreatePostController();
            var json = await ctrl.GetRole("");
            Assert.Equal("{}", json);
        }

        // ============ #35 语义回归：A 侧 rows>0 才成功 ============

        [Fact]
        public async Task UpdatePostEmpbyEid_HasPosts_AfterCallAllCleared()
        {
            // 语义回归：调用后 emp_id 全为空，default_post 全为 0
            var postId = Guid.NewGuid().ToString();
            await _fsql.Insert(NewPost(postId, "岗位", empId: "EMP_X", defaultPost: 1))
                .ExecuteAffrowsAsync();

            var ok = await _postRepo.UpdatePostEmpbyEidAsync("EMP_X");
            Assert.True(ok);

            var p = await _fsql.Select<hr_post>().Where(a => a.id == postId).FirstAsync();
            Assert.Equal(string.Empty, p.emp_id);
            Assert.Equal(0, p.default_post);
        }
    }
}
