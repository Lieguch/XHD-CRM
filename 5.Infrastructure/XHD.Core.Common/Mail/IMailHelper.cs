using System.Collections.Generic;
using System.Threading.Tasks;

namespace XHD.Core.Common.Mail
{
    /// <summary>
    /// SMTP 邮件发送抽象接口（Sprint 9 新增，#147 MailSender.SendMail）。
    /// A 侧 <c>Common/MailSender.cs</c> 有 4 个静态 Send 重载（映射表误标 SendMail），
    /// 依赖 <c>OpenSmtp.Mail</c> + <c>SmtpSetting.config</c> 文件；
    /// B 侧改用 <c>MailKit</c>，配置走构造函数注入，便于 Moq 桥接。
    /// </summary>
    public interface IMailHelper
    {
        /// <summary>
        /// 发送邮件。
        /// </summary>
        /// <param name="recipient">收件人地址（逗号分隔支持多收件人）</param>
        /// <param name="subject">邮件主题</param>
        /// <param name="body">邮件正文（isBodyHtml=true 时为 HTML）</param>
        /// <param name="isBodyHtml">正文是否 HTML 格式</param>
        /// <returns>Task&lt;bool&gt;：true 表示 SendAsync 未抛异常（不代表收件人一定收到）</returns>
        Task<bool> SendMailAsync(string recipient, string subject, string body, bool isBodyHtml = false);

        /// <summary>
        /// 发送邮件（带附件）。
        /// </summary>
        /// <param name="recipient">收件人地址</param>
        /// <param name="subject">邮件主题</param>
        /// <param name="body">邮件正文</param>
        /// <param name="isBodyHtml">正文是否 HTML 格式</param>
        /// <param name="attachments">附件绝对路径列表（可空）</param>
        /// <returns>Task&lt;bool&gt;：true 表示 SendAsync 未抛异常</returns>
        Task<bool> SendMailAsync(string recipient, string subject, string body, bool isBodyHtml, IEnumerable<string> attachments);
    }

    /// <summary>
    /// MailKit SmtpClient 封装接口（用于 DI/测试桥接）。
    /// </summary>
    public interface ISmtpSessionFactory
    {
        /// <summary>
        /// 按配置创建已连接的 SmtpClient 会话。
        /// </summary>
        /// <param name="host">SMTP 主机</param>
        /// <param name="port">SMTP 端口</param>
        /// <param name="secure">是否启用 TLS</param>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        /// <returns>已连接的会话</returns>
        object ConnectAsync(string host, int port, bool secure, string username, string password);
    }
}
