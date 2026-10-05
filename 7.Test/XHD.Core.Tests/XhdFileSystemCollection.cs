using Xunit;

namespace XHD.Core.Tests
{
    /// <summary>
    /// 文件系统隔离集合：碰 <see cref="System.Environment.CurrentDirectory"/> 的测试类必须串行执行。
    ///
    /// 根因：<see cref="System.Environment.CurrentDirectory"/> 是进程级全局状态。
    /// CustomerAttaUploadTests / SaleContractAttaUploadTests 在构造里
    /// <c>Directory.SetCurrentDirectory(唯一临时目录)</c>、Dispose 里恢复原目录；
    /// SysBaseUploadSMSTests 的导入用例读 <c>Directory.GetCurrentDirectory()</c> 拼落地目录。
    /// xUnit 默认按测试类并行，这三个类并发时会互相覆盖全局目录，轻则导入落盘到错误目录
    ///（断言失败），重则 Dispose 恢复到已被别的类删除的临时目录
    ///（DirectoryNotFoundException）。Sprint10.38 第 7 轮新增 33 个测试改变了并行调度，
    /// 把这个既有竞态从「偶发」变成「必现」。
    ///
    /// 修复方式：归入同一集合并 <c>DisableParallelization</c>——共享进程级全局状态就应当串行，
    /// 这是治本而非打补丁；不修改任何生产行为。
    /// </summary>
    [CollectionDefinition("XhdFileSystem", DisableParallelization = true)]
    public class XhdFileSystemCollection
    {
    }
}
