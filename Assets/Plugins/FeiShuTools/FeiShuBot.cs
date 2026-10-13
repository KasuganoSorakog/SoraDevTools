using System;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;

namespace SoraTools
{
    /// <summary>
    /// 飞书机器人 API 封装（使用 HttpWebRequest，适合编辑器同步调用）
    /// </summary>
    public class FeiShuBot
    {
        const string TokenUrl = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";
        const string SendMsgUrl = "https://open.feishu.cn/open-apis/im/v1/messages";

        readonly string _appId;
        readonly string _appSecret;
        string _token;

        public FeiShuBot(string appId, string appSecret)
        {
            _appId = appId;
            _appSecret = appSecret;
        }

        public string Token => _token;

        /// <summary>
        /// 获取 tenant_access_token
        /// </summary>
        public bool RefreshToken()
        {
            try
            {
                var json = $"{{\"app_id\":\"{_appId}\",\"app_secret\":\"{_appSecret}\"}}";
                Debug.Log($"[FeiShu] 正在获取 Token...");

                var response = Post(TokenUrl, json, null);
                Debug.Log($"[FeiShu] Token 响应: {response}");

                // 简易解析 tenant_access_token
                var codeVal = ParseJsonInt(response, "code");
                if (codeVal != 0)
                {
                    var msg = ParseJsonString(response, "msg");
                    Debug.LogError($"[FeiShu] Token 错误 code={codeVal}, msg={msg}");
                    return false;
                }

                _token = ParseJsonString(response, "tenant_access_token");
                if (string.IsNullOrEmpty(_token))
                {
                    Debug.LogError("[FeiShu] Token 为空，解析失败");
                    return false;
                }

                Debug.Log("[FeiShu] Token 获取成功");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FeiShu] 获取 Token 异常: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送文本消息
        /// </summary>
        /// <param name="receiveIdType">接收者类型: open_id, user_id, union_id, email, chat_id</param>
        /// <param name="receiveId">接收者 ID</param>
        /// <param name="text">消息文本</param>
        public bool SendText(string receiveIdType, string receiveId, string text)
        {
            if (string.IsNullOrEmpty(_token) && !RefreshToken())
                return false;

            try
            {
                // 手动构建 JSON，确保 content 是正确的 JSON 字符串
                var escapedText = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
                var contentJson = $"{{\\\"text\\\":\\\"{escapedText}\\\"}}";
                var body = $"{{\"receive_id\":\"{receiveId}\",\"msg_type\":\"text\",\"content\":\"{contentJson}\"}}";

                var url = $"{SendMsgUrl}?receive_id_type={receiveIdType}";

                Debug.Log($"[FeiShu] 请求 URL: {url}");
                Debug.Log($"[FeiShu] 请求 Body: {body}");

                var response = Post(url, body, _token);
                Debug.Log($"[FeiShu] 响应: {response}");

                var code = ParseJsonInt(response, "code");
                if (code != 0)
                {
                    var msg = ParseJsonString(response, "msg");
                    Debug.LogError($"[FeiShu] 发送失败 code={code}, msg={msg}\n完整响应: {response}");
                    return false;
                }

                Debug.Log($"[FeiShu] 消息发送成功 -> {receiveId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FeiShu] 发送异常: {e.Message}");
                return false;
            }
        }

/// <summary>
        /// 通过手机号查询用户 open_id
        /// 需要权限: contact:user.id:readonly（通过手机号或邮箱获取用户 ID）
        /// </summary>
        /// <param name="mobile">手机号（需要带国际区号前缀，如中国大陆 +86，例如 "+8613800138000"）</param>
        /// <returns>用户的 open_id，失败返回 null</returns>
        public string GetOpenIdByPhone(string mobile)
        {
            if (string.IsNullOrEmpty(_token) && !RefreshToken())
                return null;

            try
            {
                // 自动补 +86 前缀
                if (!mobile.StartsWith("+"))
                    mobile = "+86" + mobile;

                var url = "https://open.feishu.cn/open-apis/contact/v3/users/batch_get_id?user_id_type=open_id";
                var body = $"{{\"mobiles\":[\"{mobile}\"]}}";

                Debug.Log($"[FeiShu] 查询手机号: {mobile}");
                var response = Post(url, body, _token);
                Debug.Log($"[FeiShu] 查询响应: {response}");

                var code = ParseJsonInt(response, "code");
                if (code != 0)
                {
                    var msg = ParseJsonString(response, "msg");
                    Debug.LogError($"[FeiShu] 查询用户失败 code={code}, msg={msg}");
                    return null;
                }

                // 解析 user_id（在 user_list 数组中）
                var openId = ParseJsonString(response, "user_id");
                if (string.IsNullOrEmpty(openId))
                {
                    Debug.LogError($"[FeiShu] 未找到该手机号对应的用户，请确认手机号正确且用户在应用可用范围内");
                    return null;
                }

                Debug.Log($"[FeiShu] 查询成功: {mobile} -> {openId}");
                return openId;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FeiShu] 查询用户异常: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 通过手机号发送文本消息（自动查询 open_id 再发送）
        /// </summary>
        /// <param name="mobile">手机号（如 "13800138000" 或 "+8613800138000"）</param>
        /// <param name="text">消息文本</param>
        public bool SendTextByPhone(string mobile, string text)
        {
            var openId = GetOpenIdByPhone(mobile);
            if (string.IsNullOrEmpty(openId))
                return false;

            return SendText("open_id", openId, text);
        }


/// <summary>
        /// 获取机器人所在的群列表，返回原始 JSON
        /// </summary>
        public string ListChats()
        {
            if (string.IsNullOrEmpty(_token) && !RefreshToken())
                return null;

            try
            {
                var url = "https://open.feishu.cn/open-apis/im/v1/chats?page_size=20";
                var response = Get(url, _token);
                Debug.Log($"[FeiShu] 群列表响应: {response}");
                return response;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FeiShu] 获取群列表异常: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 同步 GET 请求
        /// </summary>
        static string Get(string url, string bearerToken)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 15000;

            if (!string.IsNullOrEmpty(bearerToken))
                request.Headers.Add("Authorization", $"Bearer {bearerToken}");

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    using (var reader = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        var errorBody = reader.ReadToEnd();
                        Debug.LogError($"[FeiShu] HTTP 错误: {ex.Message}\n响应体: {errorBody}");
                        return errorBody;
                    }
                }
                throw;
            }
        }


        /// <summary>
        /// 同步 POST 请求
        /// </summary>
        static string Post(string url, string jsonBody, string bearerToken)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Timeout = 15000;

            if (!string.IsNullOrEmpty(bearerToken))
                request.Headers.Add("Authorization", $"Bearer {bearerToken}");

            var data = Encoding.UTF8.GetBytes(jsonBody);
            request.ContentLength = data.Length;
            using (var stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                // 即使是 400/403 等错误，也要读取响应体（飞书的错误详情在里面）
                if (ex.Response != null)
                {
                    using (var reader = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        var errorBody = reader.ReadToEnd();
                        Debug.LogError($"[FeiShu] HTTP 错误: {ex.Message}\n响应体: {errorBody}");
                        return errorBody;
                    }
                }
                throw;
            }
        }

        // ---- 简易 JSON 解析（避免依赖第三方库）----

        static string ParseJsonString(string json, string key)
        {
            var pattern = $"\"{key}\":\"";
            var idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return null;
            idx += pattern.Length;
            var end = json.IndexOf("\"", idx, StringComparison.Ordinal);
            return end < 0 ? null : json.Substring(idx, end - idx);
        }

        static int ParseJsonInt(string json, string key)
        {
            var pattern = $"\"{key}\":";
            var idx = json.IndexOf(pattern, StringComparison.Ordinal);
            if (idx < 0) return -1;
            idx += pattern.Length;
            var sb = new StringBuilder();
            while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '-'))
            {
                sb.Append(json[idx]);
                idx++;
            }
            return int.TryParse(sb.ToString(), out var val) ? val : -1;
        }
    }
}
