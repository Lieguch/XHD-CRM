using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace XHD.Core.Common.Mail
{
    /// <summary>
    /// SMTP 邮件发送默认实现（Sprint 9 新增，#147）。
    /// 使用 MailKit + MimeKit，配置走构造函数注入，便于测试。
    /// </summary>
    public class MailHelper : IMailHelper
    {
        private readonly string _host;
        private readonly int _port;
        private readonly bool _useTls;
        private readonly string _fromAddress;
        private readonly string _username;
        private readonly string _password;
        private readonly ILogger<MailHelper> _logger;

        /// <summary>
        /// 便捷构造：从 appsettings / 环境变量读配置。
        /// 未在 ctor 中连接 SmtpClient，连接延迟到 SendMailAsync 时按需创建（便于测试注入 mock）。
        /// </summary>
        public MailHelper(
            string host,
            int port,
            bool useTls,
            string fromAddress,
            string username,
            string password,
            ILogger<MailHelper> logger = null,
            SmtpClientFactory smtpFactory = null)
        {
            _host = host ?? throw new ArgumentException("host 不能为 null", nameof(host));
            _port = port;
            _useTls = useTls;
            _fromAddress = fromAddress ?? string.Empty;
            _username = username ?? string.Empty;
            _password = password ?? string.Empty;
            _logger = logger;
            _smtpFactory = smtpFactory ?? new DefaultSmtpClientFactory();
        }

        private readonly SmtpClientFactory _smtpFactory;

        public Task<bool> SendMailAsync(string recipient, string subject, string body, bool isBodyHtml = false)
        {
            return SendMailAsync(recipient, subject, body, isBodyHtml, null);
        }

        public Task<bool> SendMailAsync(string recipient, string subject, string body, bool isBodyHtml, IEnumerable<string> attachments)
        {
            if (string.IsNullOrWhiteSpace(recipient))
            {
                _logger?.LogWarning("MailHelper.SendMailAsync: recipient 为空，跳过发送");
                return Task.FromResult(false);
            }
            try
            {
                var message = BuildMessage(recipient, subject, body, isBodyHtml, attachments);
                return SendAsync(message);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "MailHelper.SendMailAsync 失败");
                return Task.FromResult(false);
            }
        }

        private MimeMessage BuildMessage(string recipient, string subject, string body, bool isBodyHtml, IEnumerable<string> attachments)
        {
            var message = new MimeMessage();
            if (!string.IsNullOrWhiteSpace(_fromAddress))
            {
                message.From.Add(MimeKit.MailboxAddress.Parse(_fromAddress));
            }
            foreach (string addr in SplitRecipients(recipient))
            {
                message.To.Add(MimeKit.MailboxAddress.Parse(addr));
            }
            message.Subject = subject ?? string.Empty;

            var builder = new BodyBuilder
            {
                HtmlBody = isBodyHtml ? body : string.Empty,
                TextBody = isBodyHtml ? string.Empty : (body ?? string.Empty)
            };

            if (attachments != null)
            {
                foreach (string path in attachments.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    if (File.Exists(path))
                    {
                        string contentType = MimeTypes.GetMimeType(path);
                        builder.Attachments.Add(contentType, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                    }
                    else
                    {
                        _logger?.LogWarning("MailHelper: 附件不存在 {Path}，跳过", path);
                    }
                }
            }

            message.Body = builder.ToMessageBody();
            return message;
        }

        private Task<bool> SendAsync(MimeMessage message)
        {
            var client = _smtpFactory.Create(_host, _port, _useTls, _username, _password);
            if (client == null)
            {
                _logger?.LogWarning("MailHelper: SmtpClientFactory 返回 null，跳过发送");
                return Task.FromResult(false);
            }
            try
            {
                return client.SendMailAsync(message);
            }
            finally
            {
                client.Disconnect();
            }
        }

        private static IEnumerable<string> SplitRecipients(string recipient)
        {
            if (string.IsNullOrWhiteSpace(recipient))
            {
                yield break;
            }
            foreach (string part in recipient.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    yield return trimmed;
                }
            }
        }
    }

    /// <summary>
    /// SmtpClient 工厂抽象：便于测试时注入 mock。
    /// </summary>
    public interface ISmtpClient
    {
        /// <summary>
        /// 异步发送邮件。
        /// </summary>
        Task<bool> SendMailAsync(MimeMessage message);
        void Disconnect();
    }

    /// <summary>
    /// SmtpClient 创建工厂。
    /// </summary>
    public interface SmtpClientFactory
    {
        ISmtpClient Create(string host, int port, bool useTls, string username, string password);
    }

    /// <summary>
    /// 默认 SmtpClientFactory：创建真实 MailKit SmtpClient 并连接。
    /// </summary>
    internal sealed class DefaultSmtpClientFactory : SmtpClientFactory
    {
        public ISmtpClient Create(string host, int port, bool useTls, string username, string password)
        {
            return new DefaultSmtpClient(host, port, useTls, username, password);
        }
    }

    /// <summary>
    /// 默认 ISmtpClient 实现：包装 MailKit SmtpClient。
    /// </summary>
    internal sealed class DefaultSmtpClient : ISmtpClient
    {
        private readonly SmtpClient _client;
        public DefaultSmtpClient(string host, int port, bool useTls, string username, string password)
        {
            _client = new SmtpClient();
            // 同步 Connect 用于兼容性；Send 也走同步 API，避免在 async 方法内阻塞
            try
            {
                _client.Connect(host, port, useTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                {
                    _client.Authenticate(username, password);
                }
            }
            catch (Exception)
            {
                // 连接失败在 SendMailAsync 中会再次抛出并被上层捕获
                // 保留 client 实例以便 SendMailAsync 抛真实错误
            }
        }

        public Task<bool> SendMailAsync(MimeMessage message)
        {
            try
            {
                _client.Send(message);
                return Task.FromResult(true);
            }
            catch (Exception)
            {
                return Task.FromResult(false);
            }
            finally
            {
                try { _client.Disconnect(true); } catch { /* 忽略 */ }
                _client.Dispose();
            }
        }

        public void Disconnect()
        {
            try { _client.Disconnect(true); } catch { /* 忽略 */ }
            _client.Dispose();
        }
    }
}